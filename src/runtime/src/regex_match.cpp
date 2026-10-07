#include "regex_match.h"

#include <algorithm>
#include <cstdint>

//------------------------------------------------------------------------------
// regex_match.cpp -- Recursive backtracking matcher over the RegexAst tree
// (docs/design/regex-domain.md section 2.3), continuation-passing style.
//
// Design (verified against net48 probe runs, 2026-08-04):
//   * Leftmost-first alternation: branches tried in written order, first
//     success wins ("a|ab" vs "ab" -> "a").
//   * Greedy repeats with decreasing backtracking: repeat as many times as
//     possible, then give back one iteration at a time and retry the rest.
//   * Capture groups: finalized only when the group's child succeeds; a group
//     that never finalized -> start=-1 (unparticipated). Repeats/alternations
//     snapshot the capture stack depth and truncate on backtracking so
//     captures of abandoned iterations vanish ("(a(b)?)+" vs "a" -> group 2
//     unparticipated).
//   * CPS: every node match carries a Continuation describing what to do with
//     the position AFTER the node succeeds. This is what makes alternation
//     backtracking correct across group boundaries: "(a|ab)c" tries branch
//     'a', fails on 'c', and retries branch 'ab' -- the group finalization is
//     part of the continuation, so a failed continuation unwinds into the
//     alternation's next branch. (A plain "return position" matcher cannot do
//     this -- branch success already committed before 'c' was tried.)
//   * Step budget (kRegexMaxMatchSteps): every node visit costs one step;
//     exceeding it turns the whole match into no-match (docs section 2.4) -- net48
//     hangs on "(a*)*b" vs 50 'a' (probe timed out), we fail deterministically.
//   * "$" (non-Multiline) matches end-of-string OR immediately before a
//     trailing '\n' (net48 probe: "a$" matches "a\n").
//   * Bumpalong: try every start position 0..len; "leftmost" not "longest".
//------------------------------------------------------------------------------

namespace il2cpp { namespace runtime {

namespace {

struct Capture {
    int32_t group;
    int32_t start;
    int32_t length;
};

struct MatchContext {
    const RegexAst* ast;
    const char16_t* text;
    int32_t len;
    int64_t steps;
    bool budgetHit;
    std::vector<Capture> captures; // finalized capture history (stack)
    int32_t base;                  // absolute offset of text[0] (anchor math)
    const char16_t* origin;        // absolute text start (LineStart lookback)
};

char16_t Fold(char16_t c)
{
    if (c >= u'A' && c <= u'Z')
    {
        return static_cast<char16_t>(c + (u'a' - u'A')); // ASCII only (docs section 1.4-3)
    }
    return c;
}

bool RangeContains(const RegexNode* node, char16_t c)
{
    if (node->ignoreCase)
    {
        // ASCII folding applies to BOTH the probe char and the range bounds
        // (net48: "[A-Z]" with IgnoreCase matches 'a').
        c = Fold(c);
    }
    for (int32_t i = 0; i < node->rangeCount; ++i)
    {
        const char16_t lo = node->ignoreCase ? Fold(node->ranges[i].lo) : node->ranges[i].lo;
        const char16_t hi = node->ignoreCase ? Fold(node->ranges[i].hi) : node->ranges[i].hi;
        if (c >= lo && c <= hi)
        {
            return true;
        }
    }
    return false;
}

bool SetContains(const RegexNode* node, char16_t c)
{
    const bool in = RangeContains(node, c);
    return node->negate ? !in : in;
}

//------------------------------------------------------------------------------
// CPS machinery
//------------------------------------------------------------------------------

// A Continuation describes what to do with the position AFTER a node
// succeeds: (a) finalize a capture group (if groupToFinalize >= 1), then
// (b) match the rest of a parent sequence (children[index..count-1]), then
// (c) the continuation that was pending when that sequence was entered
// (parent -- chained, so the whole pending work is a linked list).
// children == nullptr && parent == nullptr means "match complete" (top).
// repeat: non-null = the repeat loop is waiting on this node (alternation
// give-back, D-REG-1). When a node whose continuation carries a RepeatState
// succeeds, Continue() re-enters the repeat loop from the resulting position
// instead of running children/parent -- the node was matched AGAIN from the
// start of an iteration so an alternation inside the repeat can try a later
// branch. Group finalization (groupToFinalize) still runs FIRST so the
// re-matched iteration's captures are recorded before the loop resumes.
struct RepeatState;
struct Continuation {
    const RegexNode* const* children; // nullptr = no sequence to run
    int32_t count;
    int32_t index;
    int32_t groupToFinalize; // >= 1: finalize this group with (groupStart, pos)
    int32_t groupStart;
    const Continuation* parent; // what to do after children[index..] finishes
    RepeatState* repeat;        // non-null: re-enter the repeat loop at pos
};

// Returns the final end position on success, or -1 on failure.
int32_t MatchNode(const RegexNode* node, int32_t pos, MatchContext* ctx,
                  const Continuation& cont);
int32_t MatchSequence(const RegexNode* const* children, int32_t count, int32_t index,
                      int32_t pos, MatchContext* ctx, const Continuation& cont);
int32_t MatchRepeat(const RegexNode* node, int32_t pos, MatchContext* ctx,
                    const RegexNode* const* children, int32_t count, int32_t index,
                    const Continuation& cont);
int32_t MatchRepeatCore(RepeatState* st, int32_t pos, MatchContext* ctx);
int32_t MatchRepeatCoreInner(RepeatState* st, int32_t pos, MatchContext* ctx);
int32_t GiveBackIteration(RepeatState* st, int32_t k, MatchContext* ctx);

// One completed repeat iteration: the position reached after it and the
// capture depth BEFORE it started (trail[i].capMark = state after iterations
// 1..i). Kept in the RepeatState so greedy extension and the resume path
// share one trail for the whole loop.
struct Trail {
    int32_t pos;
    size_t capMark;

    bool operator==(const Trail& o) const
    {
        return pos == o.pos && capMark == o.capMark;
    }
};

// Mutable state for ONE MatchRepeat invocation (a fresh RepeatState per loop,
// including nested resumes which share the trail). Living on the caller's
// stack; all resumes fire synchronously within that frame.
struct RepeatState {
    const RegexNode* node;        // the repeat node (node->child is the body)
    const RegexNode* const* children; // parent sequence (nullptr for bare repeat)
    int32_t count;                // parent sequence length
    int32_t index;                // index of this repeat within the sequence
    Continuation cont;            // the pending continuation (outer)
    size_t baseMark;              // capture depth when the loop was entered
    int32_t entryPos;             // position where the loop was entered
    int32_t depth;                // resume depth (stack guard, see below)
    size_t pendingCapMark;        // capture mark of the iteration being re-matched
    int32_t pendingOriginalEnd;   // end position the iteration had BEFORE give-back
    int32_t pendingOriginalStart; // start position of the iteration being re-matched
    std::vector<Trail> trail;     // completed iterations (append / resize)
    // Post-greedy trail snapshots of the ACTIVE resume chain (this core and
    // every ancestor). A resume that re-enters with a post-greedy trail equal
    // to an ancestor's is revisiting an already-explored state -- the ancestor
    // explored it completely and failed, so the resume can only repeat that
    // failure (and would otherwise loop forever on give-back cycles like
    // "(a|ab)+$" vs "ab"+"b"*22+"ab", where re-matching iteration 1 toggles
    // between 'a'@1 and 'ab'@2 forever).
    std::vector<std::vector<Trail>> snapshots;
};

} // namespace

namespace {

int32_t Continue(const Continuation& cont, int32_t pos, MatchContext* ctx)
{
    if (cont.groupToFinalize >= 1)
    {
        ctx->captures.push_back({cont.groupToFinalize, cont.groupStart,
                                 pos - cont.groupStart});
    }
    if (cont.repeat != nullptr)
    {
        // Alternation give-back: the node just matched from the start of an
        // iteration; record it and re-enter the repeat loop at 'pos'.
        RepeatState* st = cont.repeat;
        if (pos == st->pendingOriginalEnd || pos == st->pendingOriginalStart)
        {
            // (a) Same end as the given-back iteration: the outer loop ALREADY
            // tried the continuation at this position and it failed, and any
            // nested exploration would revisit the same states -- skip (only a
            // DIFFERENT end position can change the outcome).
            // (b) An EMPTY re-match (end == start): like the greedy-phase
            // empty-iteration guard, an empty iteration cannot change the
            // outcome and would only grow the trail without progress.
            // Both prevent redundant recursion / unbounded trails (D-REG-1).
            return -1;
        }
        const size_t pre = st->trail.size();
        st->trail.push_back({pos, st->pendingCapMark});
        const int32_t r = MatchRepeatCore(st, pos, ctx);
        if (r < 0)
        {
            st->trail.resize(pre); // abandoned re-match must not leak iterations
        }
        return r;
    }
    if (cont.children == nullptr)
    {
        return cont.parent != nullptr ? Continue(*cont.parent, pos, ctx) : pos;
    }
    return MatchSequence(cont.children, cont.count, cont.index, pos, ctx,
                         *cont.parent);
}

int32_t MatchNode(const RegexNode* node, int32_t pos, MatchContext* ctx,
                  const Continuation& cont)
{
    if (ctx->budgetHit)
    {
        return -1;
    }
    if (++ctx->steps > kRegexMaxMatchSteps)
    {
        ctx->budgetHit = true;
        return -1;
    }

    switch (node->kind)
    {
    case RegexNodeKind::Empty:
        return Continue(cont, pos, ctx);

    case RegexNodeKind::Char:
    {
        if (pos >= ctx->len)
        {
            return -1;
        }
        char16_t want = node->ch;
        char16_t got = ctx->text[pos];
        if (node->ignoreCase)
        {
            want = Fold(want);
            got = Fold(got);
        }
        return want == got ? Continue(cont, pos + 1, ctx) : -1;
    }

    case RegexNodeKind::Any:
    {
        if (pos >= ctx->len)
        {
            return -1;
        }
        if (ctx->text[pos] == u'\n' &&
            (ctx->ast->options & kRegexOptionsSingleline) == 0)
        {
            return -1;
        }
        return Continue(cont, pos + 1, ctx);
    }

    case RegexNodeKind::Set:
    {
        if (pos >= ctx->len)
        {
            return -1;
        }
        return SetContains(node, ctx->text[pos]) ? Continue(cont, pos + 1, ctx) : -1;
    }

    case RegexNodeKind::Anchor:
        switch (node->anchor)
        {
        case AnchorKind::Start:
            // Absolute position 0 only (a suffix slice must NOT re-anchor).
            return pos + ctx->base == 0 ? Continue(cont, pos, ctx) : -1;
        case AnchorKind::End:
            // net48: end-of-string OR immediately before a trailing '\n'.
            if (pos == ctx->len)
            {
                return Continue(cont, pos, ctx);
            }
            if (pos == ctx->len - 1 && ctx->text[pos] == u'\n')
            {
                return Continue(cont, pos, ctx);
            }
            return -1;
        case AnchorKind::LineStart:
        {
            const int32_t abs = pos + ctx->base;
            if (abs == 0)
            {
                return Continue(cont, pos, ctx);
            }
            return ctx->origin[abs - 1] == u'\n' ? Continue(cont, pos, ctx) : -1;
        }
        case AnchorKind::LineEnd:
            if (pos == ctx->len)
            {
                return Continue(cont, pos, ctx);
            }
            return ctx->text[pos] == u'\n' ? Continue(cont, pos, ctx) : -1;
        }
        return -1;

    case RegexNodeKind::Backref:
    {
        // net48 semantics (probe-verified 2026-08-08): the backreference reads
        // the CURRENT value of the referenced group -- the most recent
        // finalized capture for that group. A group that has NOT participated
        // makes the backref FAIL ("(a)?\1" vs "b" -> no match, group unset);
        // a group that captured the empty string makes the backref match empty
        // ("(a*)b\1" vs "ab" -> matches, g1 = "").
        int32_t capStart = -1;
        int32_t capLen = 0;
        for (size_t i = ctx->captures.size(); i-- > 0;)
        {
            if (ctx->captures[i].group == node->backrefGroup)
            {
                capStart = ctx->captures[i].start;
                capLen = ctx->captures[i].length;
                break;
            }
        }
        if (capStart < 0)
        {
            return -1; // group never participated -> backref fails
        }
        if (pos + capLen > ctx->len)
        {
            return -1;
        }
        for (int32_t i = 0; i < capLen; ++i)
        {
            char16_t want = ctx->text[capStart + i];
            char16_t got = ctx->text[pos + i];
            if (node->ignoreCase)
            {
                want = Fold(want);
                got = Fold(got);
            }
            if (want != got)
            {
                return -1;
            }
        }
        return Continue(cont, pos + capLen, ctx);
    }

    case RegexNodeKind::Group:
    {
        // The group's child is matched with a continuation that finalizes
        // THIS group then runs the outer continuation. If the outer
        // continuation fails, control unwinds back to the child's own
        // backtracking points (e.g. an alternation inside the group retries
        // its next branch) -- captures made by abandoned paths are truncated
        // by those backtracking points.
        const size_t mark = ctx->captures.size();
        Continuation groupCont;
        groupCont.children = cont.children;
        groupCont.count = cont.count;
        groupCont.index = cont.index;
        groupCont.groupToFinalize = node->groupIndex;
        groupCont.groupStart = pos;
        groupCont.parent = cont.parent;
        groupCont.repeat = cont.repeat;
        const int32_t end = MatchNode(node->child, pos, ctx, groupCont);
        if (end < 0)
        {
            ctx->captures.resize(mark); // rollback anything the child pushed
            return -1;
        }
        return end;
    }

    case RegexNodeKind::Sequence:
        return MatchSequence(node->children, node->childCount, 0, pos, ctx, cont);

    case RegexNodeKind::Alternation:
    {
        // Leftmost-first: try each branch with the FULL continuation. A
        // branch that matches but whose continuation fails is discarded
        // (captures truncated) and the next branch is tried -- this is what
        // makes "(a|ab)c" succeed via branch 'ab'.
        for (int32_t i = 0; i < node->childCount; ++i)
        {
            const size_t mark = ctx->captures.size();
            const int32_t end = MatchNode(node->children[i], pos, ctx, cont);
            if (end >= 0)
            {
                return end;
            }
            ctx->captures.resize(mark); // discard this branch's captures
        }
        return -1;
    }

    case RegexNodeKind::Repeat:
        return MatchRepeat(node, pos, ctx, nullptr, 0, 0, cont);
    }
    return -1;
}

// Matches children[index..count-1] in order, then the continuation.
int32_t MatchSequence(const RegexNode* const* children, int32_t count, int32_t index,
                      int32_t pos, MatchContext* ctx, const Continuation& cont)
{
    if (ctx->budgetHit)
    {
        return -1;
    }
    if (index >= count)
    {
        return Continue(cont, pos, ctx);
    }
    const RegexNode* child = children[index];
    if (child->kind == RegexNodeKind::Repeat)
    {
        // Repeats own their backtracking: after giving back an iteration they
        // must retry the REST of the sequence, so the continuation is the
        // remainder of this children array plus the pending continuation.
        return MatchRepeat(child, pos, ctx, children, count, index, cont);
    }
    Continuation childCont;
    childCont.children = children;
    childCont.count = count;
    childCont.index = index + 1;
    childCont.groupToFinalize = -1;
    childCont.groupStart = 0;
    childCont.parent = &cont;
    childCont.repeat = nullptr;
    return MatchNode(child, pos, ctx, childCont);
}

// Greedy repeat with decreasing backtracking (docs section 2.3 MatchRepeat),
// implemented with an ITERATIVE greedy phase: the greedy loop records one
// trail entry per consumed iteration (position reached + capture depth before
// the iteration); the backtrack phase walks the trail downwards and re-runs
// the continuation (the parent sequence's remainder) after each give-back.
// Iterative form is required so the call depth does not scale with the input
// length ("a+" on 100k chars must not recurse 100k frames).
//
// ALTERNATION GIVE-BACK (D-REG-1, 2026-08-12): when the continuation fails at
// count k, the loop does not just drop the last iteration -- it re-matches
// iteration k's child AGAIN from the start of that iteration, passing a
// "resume" continuation that re-enters the loop from the child's new end
// position (Continuation::repeat -> MatchRepeatCore). This lets an alternation
// whose FIRST branch is a strict prefix of a later branch give the prefix back
// and try the later branch: "^(a|ab)+$" vs "abab" requires iterations
// 'ab'+'ab' (net48 oracle: group1 = "ab" at 2), which the old code could never
// reach because every iteration had already committed to 'a'. The re-match
// goes through MatchNode with the full CPS continuation, so ALL of the child's
// internal backtracking points (nested alternations, inner repeats, groups)
// participate -- not just the top-level alternation.
//
// Empty iteration guard: when an iteration matches empty (child matched zero
// chars, e.g. "(a*)*" after the inner star gave everything back) and min is
// already satisfied, iteration stops -- repeating an empty match can never
// change the outcome (net48 semantics: empty repeats terminate). While
// count < min, empty iterations are still consumed to reach min.
//
// LAZY repeats (node->lazy, "a*?" "a+?" "a??" "{n,m}?"): the same pre-scan
// records how many iterations are achievable; the lazy path then tries the
// MINIMUM count first and adds one iteration at a time ONLY when the
// continuation demands it (net48 semantics, probe-verified 2026-08-08:
// "(a+?)b" vs "aab" -> group1="aa", "(a{2,3}?)b" vs "aaaab" -> "aaab"@1).
// When the counts are exhausted it uses the same alternation give-back
// ("(a|ab)+?$" vs "abab" -> group1 = "ab" at 2, net48 oracle).
//
// CAPTURE ROLLBACK (conformance fix 2026-08-08): giving back from iters to k
// must keep iterations 1..k, so the resize target is the mark taken BEFORE
// iteration k+1 (trail[k].capMark), NOT the mark before iteration k
// (trail[k-1].capMark) -- the old target silently dropped iteration k's own
// capture, so "(ab)+ab" vs "ababab" reported group1=(0,2) instead of net48's
// (2,2), and "(a)+a" vs "aa" reported group1 unparticipated (-1,0) instead of
// (0,1). Verified against net48 probe (2026-08-08). The re-match path rolls
// back to trail[k-1].capMark (before iteration k) and lets the re-matched
// child's own backtracking points truncate on failure.
int32_t MatchRepeat(const RegexNode* node, int32_t pos, MatchContext* ctx,
                    const RegexNode* const* children, int32_t count, int32_t index,
                    const Continuation& cont)
{
    RepeatState st;
    st.node = node;
    st.children = children;
    st.count = count;
    st.index = index;
    st.cont = cont;
    st.baseMark = ctx->captures.size();
    st.entryPos = pos;
    st.depth = 0;
    st.pendingCapMark = 0;
    st.pendingOriginalEnd = -1;
    st.pendingOriginalStart = -1;
    st.trail.reserve(64);
    return MatchRepeatCore(&st, pos, ctx);
}

// Re-match iteration 'k' (1-based) of the trail from the start of that
// iteration, passing a "resume" continuation (D-REG-1). The child is matched
// AGAIN with the full continuation, so any branch other than the one the
// greedy phase committed to can now be chosen; on success the continuation
// re-enters MatchRepeatCore from the new end position. On total failure the
// trail and captures are left as if the iteration had been given back (the
// child's backtracking points roll captures back to trail[k-1].capMark).
int32_t GiveBackIteration(RepeatState* st, int32_t k, MatchContext* ctx)
{
    if (ctx->budgetHit)
    {
        return -1;
    }
    std::vector<Trail>& trail = st->trail;
    const int32_t startOfIterK = (k == 1) ? st->entryPos : trail[k - 2].pos;
    st->pendingOriginalEnd = trail[k - 1].pos; // end BEFORE the give-back
    st->pendingOriginalStart = startOfIterK;   // the iteration being re-matched
    ctx->captures.resize(trail[k - 1].capMark); // roll back to before iteration k
    st->pendingCapMark = trail[k - 1].capMark;
    trail.resize(k - 1);
    Continuation resume;
    resume.children = nullptr;
    resume.count = 0;
    resume.index = 0;
    resume.groupToFinalize = -1;
    resume.groupStart = 0;
    resume.parent = nullptr;
    resume.repeat = st;
    return MatchNode(st->node->child, startOfIterK, ctx, resume);
}

// Depth guard for the resume recursion: one level is added per alternation
// give-back that re-enters the loop. Realistic patterns stay within a few
// dozen; pathological inputs (which net48 either hangs on or needs the step
// budget to kill) hit this limit first and fail deterministically, matching
// the step-budget philosophy in docs section 2.4.
constexpr int32_t kRepeatMaxResumeDepth = 200;

int32_t MatchRepeatCore(RepeatState* st, int32_t pos, MatchContext* ctx)
{
    if (ctx->budgetHit)
    {
        return -1;
    }
    if (++st->depth > kRepeatMaxResumeDepth)
    {
        --st->depth;
        return -1;
    }
    const size_t entryTrail = st->trail.size();
    const int32_t r = MatchRepeatCoreInner(st, pos, ctx);
    --st->depth;
    if (r < 0)
    {
        // Restore the trail to the entry state. The give-back guard below
        // (BacktrackGreedy/Lazy never pop below entryCount) guarantees the
        // trail never shrank below entryTrail, so this resize only removes
        // this core's own greedy additions -- never re-grows with garbage.
        st->trail.resize(entryTrail);
    }
    return r;
}

int32_t BacktrackLazy(RepeatState* st, int32_t iters, int32_t entryCount, MatchContext* ctx);
int32_t BacktrackGreedy(RepeatState* st, int32_t iters, int32_t entryCount, MatchContext* ctx);

int32_t MatchRepeatCoreInner(RepeatState* st, int32_t pos, MatchContext* ctx)
{
    const RegexNode* node = st->node;
    std::vector<Trail>& trail = st->trail;
    const int32_t min = node->min;
    const int32_t max = node->max;
    // Number of iterations already committed by the PARENT (the trail entries
    // this core may read but never give back -- their give-back is the parent's
    // job, and popping them would destroy data the parent still needs).
    const int32_t entryCount = static_cast<int32_t>(trail.size());

    // ---- greedy phase: consume as many iterations as possible ----
    int32_t cur = pos;
    int32_t iters = static_cast<int32_t>(trail.size());
    while (max == -1 || iters < max)
    {
        if (ctx->budgetHit)
        {
            return -1;
        }
        const size_t mark = ctx->captures.size();
        const int32_t before = cur;
        const Continuation emptyCont = {nullptr, 0, 0, -1, 0, nullptr, nullptr};
        const int32_t next = MatchNode(node->child, cur, ctx, emptyCont);
        if (next < 0)
        {
            break; // iteration failed; give back from here
        }
        trail.push_back({next, mark});
        cur = next;
        ++iters;
        if (next == before && iters >= min)
        {
            break; // empty iteration: more iterations cannot change the result
        }
    }

    if (iters < min)
    {
        return -1; // could not reach the minimum count
    }

    // ---- state-revisit guard (D-REG-1) ----
    // This core's post-greedy trail fully determines its exploration (the
    // continuation runs from trail[k-1].pos and give-backs re-match from
    // trail[k-2].pos). If an ACTIVE ancestor already explored this exact trail
    // and failed, this core can only repeat that failure -- skip it. Without
    // this, give-back cycles loop forever: "(a|ab)+$" vs "ab"+"b"*22+"ab"
    // re-matches iteration 1 to 'ab' (end 2), then back to 'a' (end 1), each
    // resume re-entering a state an ancestor already exhausted.
    for (const auto& s : st->snapshots)
    {
        if (s.size() == trail.size() &&
            std::equal(s.begin(), s.end(), trail.begin()))
        {
            return -1;
        }
    }
    st->snapshots.push_back(trail);
    const int32_t r = node->lazy ? BacktrackLazy(st, iters, entryCount, ctx)
                                 : BacktrackGreedy(st, iters, entryCount, ctx);
    st->snapshots.pop_back();
    return r;
}

// ---- LAZY: minimum count first, expand one iteration at a time ----
int32_t BacktrackLazy(RepeatState* st, int32_t iters, int32_t entryCount, MatchContext* ctx)
{
    const RegexNode* node = st->node;
    std::vector<Trail>& trail = st->trail;
    const int32_t min = node->min;
    const size_t baseMark = st->baseMark;
    const int32_t entryPos = st->entryPos;

    // Snapshot the full greedy captures (after iterations 1..iters): the
    // ascending sweep resizes captures DOWN when trying a smaller count, and
    // must RESTORE them when the count grows again -- otherwise iteration k's
    // capture is lost ("^(a|ab)+?$" vs "aab": the re-matched second iteration
    // 'ab' must be the reported group1, not the stale first iteration).
    const std::vector<Capture> fullCaps = ctx->captures;

    for (int32_t k = min; k <= iters; ++k)
    {
        if (ctx->budgetHit)
        {
            return -1;
        }
        const int32_t p = (k == 0) ? entryPos : trail[k - 1].pos;
        // Keep iterations 1..k: the mark BEFORE iteration k+1 (trail[k].capMark)
        // preserves 1..k; for the full count (k == iters) restore the greedy
        // captures. (A stale continuation attempt may have pushed captures
        // that a later count must not see -- the full reset also clears them.)
        if (k == iters)
        {
            ctx->captures = fullCaps;
        }
        else if (k == 0)
        {
            ctx->captures.resize(baseMark);
        }
        else
        {
            ctx->captures = fullCaps;
            ctx->captures.resize(trail[k].capMark);
        }
        const int32_t r = st->children != nullptr
            ? MatchSequence(st->children, st->count, st->index + 1, p, ctx, st->cont)
            : Continue(st->cont, p, ctx);
        if (r >= 0)
        {
            return r;
        }
    }
    // Expansion exhausted: give the last iteration back into its own
    // alternation before failing (D-REG-1). Never give back an iteration the
    // parent committed (index <= entryCount) -- that give-back is the parent's
    // and popping it would destroy data the parent still needs.
    if (iters > entryCount)
    {
        const int32_t r2 = GiveBackIteration(st, iters, ctx);
        if (r2 >= 0)
        {
            return r2;
        }
    }
    return -1;
}

// ---- GREEDY backtrack ----
// Give back iterations one at a time; after each give-back the remaining
// iterations count (k) must satisfy min, and the continuation (rest of the
// parent sequence) must match from the trail position. Before dropping an
// iteration entirely, re-match it from its start so an alternation can
// try its later branches (D-REG-1).
int32_t BacktrackGreedy(RepeatState* st, int32_t iters, int32_t entryCount, MatchContext* ctx)
{
    const RegexNode* node = st->node;
    std::vector<Trail>& trail = st->trail;
    const int32_t min = node->min;
    const int32_t entryPos = st->entryPos;

    for (int32_t k = iters; k >= min; --k)
    {
        if (ctx->budgetHit)
        {
            return -1;
        }
        const int32_t p = (k == 0) ? entryPos : trail[k - 1].pos;
        if (st->children != nullptr)
        {
            const int32_t r = MatchSequence(st->children, st->count, st->index + 1, p, ctx, st->cont);
            if (r >= 0)
            {
                return r;
            }
        }
        else
        {
            // Bare repeat (whole pattern, or a group whose only child is the
            // repeat): the pending continuation MUST still run -- it carries
            // the group finalization and any enclosing sequence remainder.
            // (Found by the conformance audit: "(\d+)-(\d+)" never captured
            // its groups and skipped the rest of the pattern. For the whole
            // pattern the continuation is the trivial top, so Continue == p.)
            const int32_t r = Continue(st->cont, p, ctx);
            if (r >= 0)
            {
                return r;
            }
        }
        if (k > entryCount)
        {
            // Give back iteration k (re-match it from its start so an
            // alternation can try its later branches, D-REG-1) -- but NEVER an
            // iteration the parent committed (index <= entryCount): that
            // give-back is the parent's, and popping it would destroy data the
            // parent still needs (and regrow the trail with garbage).
            const int32_t r2 = GiveBackIteration(st, k, ctx);
            if (r2 >= 0)
            {
                return r2;
            }
        }
    }
    return -1;
}

} // namespace

//------------------------------------------------------------------------------
// Entry point
//------------------------------------------------------------------------------

bool RegexMatchAt(const RegexAst* pattern, const char16_t* input,
                  int32_t inputLength, int32_t baseOffset, MatchResult* outResult)
{
    if (outResult != nullptr)
    {
        outResult->success = false;
        outResult->start = -1;
        outResult->matchedLength = 0;
        outResult->groups.clear();
    }
    if (pattern == nullptr || pattern->root == nullptr || input == nullptr ||
        inputLength < 0)
    {
        return false;
    }

    MatchContext ctx;
    ctx.ast = pattern;
    ctx.text = input;
    ctx.len = inputLength;
    ctx.steps = 0;
    ctx.budgetHit = false;
    ctx.base = baseOffset;
    ctx.origin = input;


    const Continuation top = {nullptr, 0, 0, -1, 0, nullptr, nullptr};

    // Bumpalong: leftmost position wins.
    for (int32_t start = 0; start <= ctx.len; ++start)
    {
        ctx.captures.clear();
        const int32_t end = MatchNode(pattern->root, start, &ctx, top);
        if (end >= 0)
        {
            if (outResult != nullptr)
            {
                outResult->success = true;
                outResult->start = start;
                outResult->matchedLength = end - start;
                outResult->groups.assign(pattern->groupCount,
                                         RegexGroupCapture{-1, 0});
                for (const Capture& cap : ctx.captures)
                {
                    if (cap.group >= 1 && cap.group <= pattern->groupCount)
                    {
                        outResult->groups[cap.group - 1] =
                            RegexGroupCapture{cap.start, cap.length};
                    }
                }
            }
            return true;
        }
        if (ctx.budgetHit)
        {
            break; // deterministic no-match (docs section 2.4)
        }
    }
    return false;
}

bool il2cpp_regex_match(const RegexAst* pattern, const char16_t* input,
                        int32_t inputLength, MatchResult* outResult)
{
    return RegexMatchAt(pattern, input, inputLength, 0, outResult);
}

} } // namespace il2cpp::runtime






