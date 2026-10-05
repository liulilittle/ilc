//------------------------------------------------------------------------------
// regexunicode.cs -- System.Text.RegularExpressions UNICODE DEEP reference
// program (net48 oracle, byte-for-byte). Created in e2e batch C57 (2026-08-12),
// AFTER the regex domain wiring (MethodMap.Regex.cs -> regex_api.cpp:
// IsMatch/Match/Split/Replace over regex_parse.cpp / regex_match.cpp; the
// regexdeep2 baseline covered char classes / groups / backtracking / literal
// CJK). This case goes DEEPER on the Unicode axis:
//
//   1. CJK LITERALS + ANCHORS: literal hanzi in pattern/input, ^/$ anchors
//      with CJK, CJK + ASCII in one anchored pattern.
//   2. UNICODE CHAR-CLASS RANGES (the supported \p{...} replacement -- \p{L}
//      etc. are out of subset, see GAPS): [一-龥] CJK, [ぁ-ん] hiragana,
//      [ァ-ヶ] katakana, [가-힣] hangul, [０-９] fullwidth digits, a mixed
//      half/full-width ASCII digit range, Greek [α-ω], plus negation and a
//      CJK+ASCII union class. (All BMP single code units: net48 char-class
//      range semantics == code-unit semantics, so both engines agree.)
//   3. CJK QUANTIFIERS: {n} / {n,} / {n,m} and * + over literal hanzi, empty
//      input with *.
//   4. CJK GROUPS + REPLACEMENT: $1/$2 reorder of CJK groups, literal CJK
//      replacement, CJK separator replacement.
//   5. CJK + \d (ASCII digits in both engines) mixed patterns, . on BMP CJK
//      (single code unit -- SAFE, unlike surrogate-pair input, see GAPS).
//   6. CJK SPLIT: literal hanzi separator, count + element checks.
//   7. EMOJI (non-BMP surrogate-pair LITERALS ONLY): literal emoji in
//      pattern/input/replacement, Replace/Split/IsMatch with literal emoji.
//      NO . / [class] / quantifier / {n} probes on surrogate-pair input --
//      the runtime Set/Any nodes match ONE char16_t unit while net48 treats
//      a surrogate pair as one char (see GAPS D-REG-UNI-2).
//
// Output stays ASCII bool only (the regexdeep2 precedent): no wide-char diff
// risk even with UTF-8 pinned on both sides.
//------------------------------------------------------------------------------
using System;
using System.Text.RegularExpressions;
namespace Cases { public static class RegexUnicode {
    public static void Main() {
        // ============ 1. CJK literals + anchors ============
        Console.WriteLine(Regex.IsMatch("中文测试", "中文") == true);
        Console.WriteLine(Regex.IsMatch("中文测试", "测试") == true);
        Console.WriteLine(Regex.IsMatch("中文测试", "英文") == false);
        Console.WriteLine(Regex.IsMatch("中文测试", "中文测试") == true);
        Console.WriteLine(Regex.IsMatch("中文测试", "^中文") == true);
        Console.WriteLine(Regex.IsMatch("中文测试", "测试$") == true);
        Console.WriteLine(Regex.IsMatch("中文测试", "^测试$") == false);
        Console.WriteLine(Regex.IsMatch("中文", "^中文$") == true);
        Console.WriteLine(Regex.IsMatch("中文a", "^中文$") == false);

        // ============ 2. Unicode char-class ranges ============
        Console.WriteLine(Regex.IsMatch("中", "[一-龥]") == true);
        Console.WriteLine(Regex.IsMatch("龥", "[一-龥]") == true);
        Console.WriteLine(Regex.IsMatch("A", "[一-龥]") == false);
        Console.WriteLine(Regex.IsMatch("中", "[^一-龥]") == false);
        Console.WriteLine(Regex.IsMatch("A", "[^一-龥]") == true);
        Console.WriteLine(Regex.IsMatch("中A1", "[一-龥A-Z0-9]+") == true);
        Console.WriteLine(Regex.IsMatch("中A1", "^[一-龥a-z]+$") == false);
        Console.WriteLine(Regex.IsMatch("あ", "[ぁ-ん]") == true);
        Console.WriteLine(Regex.IsMatch("ん", "[ぁ-ん]") == true);
        Console.WriteLine(Regex.IsMatch("ア", "[ぁ-ん]") == false);
        Console.WriteLine(Regex.IsMatch("ア", "[ァ-ヶ]") == true);
        Console.WriteLine(Regex.IsMatch("ヶ", "[ァ-ヶ]") == true);
        Console.WriteLine(Regex.IsMatch("a", "[ァ-ヶ]") == false);
        Console.WriteLine(Regex.IsMatch("한", "[가-힣]") == true);
        Console.WriteLine(Regex.IsMatch("힣", "[가-힣]") == true);
        Console.WriteLine(Regex.IsMatch("中", "[가-힣]") == false);
        Console.WriteLine(Regex.IsMatch("１", "[０-９]") == true);
        Console.WriteLine(Regex.IsMatch("９", "[０-９]") == true);
        Console.WriteLine(Regex.IsMatch("1", "[０-９]") == false);
        Console.WriteLine(Regex.IsMatch("1", "[0-９]") == true);
        Console.WriteLine(Regex.IsMatch("中", "[１-９]") == false);
        Console.WriteLine(Regex.IsMatch("１", "[１-９]") == true);
        Console.WriteLine(Regex.IsMatch("α", "[α-ω]") == true);
        Console.WriteLine(Regex.IsMatch("ω", "[α-ω]") == true);
        Console.WriteLine(Regex.IsMatch("a", "[α-ω]") == false);

        // ============ 3. CJK quantifiers ============
        Console.WriteLine(Regex.IsMatch("哈哈哈", "哈{3}") == true);
        Console.WriteLine(Regex.IsMatch("哈哈", "哈{3}") == false);
        Console.WriteLine(Regex.IsMatch("哈哈哈哈哈", "^哈+$") == true);
        Console.WriteLine(Regex.IsMatch("哈哈", "^哈{1,3}$") == true);
        Console.WriteLine(Regex.IsMatch("哈哈哈哈", "^哈{1,3}$") == false);
        Console.WriteLine(Regex.IsMatch("", "哈*") == true);
        Console.WriteLine(Regex.IsMatch("哈哈", "哈*") == true);
        Console.WriteLine(Regex.IsMatch("哈", "哈?") == true);
        Console.WriteLine(Regex.IsMatch("", "哈?") == true);

        // ============ 4. CJK groups + replacement ============
        Console.WriteLine(Regex.Replace("你好世界", "(你好)(世界)", "$2$1") == "世界你好");
        Console.WriteLine(Regex.Replace("你好世界", "好世", "X") == "你X界");
        Console.WriteLine(Regex.Replace("a中b", "中", "c") == "acb");
        Console.WriteLine(Regex.Replace("甲/乙/丙", "/", "-") == "甲-乙-丙");
        Console.WriteLine(Regex.Replace("苹果苹果", "苹果", "梨") == "梨梨");

        // ============ 5. CJK + ASCII digits / dot ============
        Console.WriteLine(Regex.IsMatch("abc中文123", "^[a-z]+中文\\d+$") == true);
        Console.WriteLine(Regex.IsMatch("abc中文abc", "^[a-z]+中文\\d+$") == false);
        Console.WriteLine(Regex.IsMatch("版本3", "\\d") == true);
        Console.WriteLine(Regex.IsMatch("版本", "\\d") == false);
        Console.WriteLine(Regex.Replace("第1章第2节", "\\d", "X") == "第X章第X节");
        Console.WriteLine(Regex.IsMatch("中", ".") == true);
        Console.WriteLine(Regex.IsMatch("中文", "^..$") == true);
        Console.WriteLine(Regex.IsMatch("中", "^..$") == false);
        Console.WriteLine(Regex.IsMatch("a中文b", "a.*b") == true);

        // ============ 6. CJK Split ============
        Console.WriteLine(Regex.Split("a中b中c", "中").Length == 3);
        Console.WriteLine(Regex.Split("a中b中c", "中")[0] == "a");
        Console.WriteLine(Regex.Split("a中b中c", "中")[1] == "b");
        Console.WriteLine(Regex.Split("a中b中c", "中")[2] == "c");
        Console.WriteLine(Regex.Split("中a", "中").Length == 2);
        Console.WriteLine(Regex.Split("中a", "中")[0] == "");

        // ============ 7. Emoji (surrogate-pair literals only) ============
        Console.WriteLine(Regex.IsMatch("a😀b", "😀") == true);
        Console.WriteLine(Regex.IsMatch("😀", "😀") == true);
        Console.WriteLine(Regex.IsMatch("a😀b", "🙂") == false);
        Console.WriteLine(Regex.IsMatch("😀😀", "😀") == true);
        Console.WriteLine(Regex.Replace("a😀b", "😀", "x") == "axb");
        Console.WriteLine(Regex.Replace("😀😀", "😀", "-") == "--");
        Console.WriteLine(Regex.Replace("a😀b", "a", "😀") == "😀😀b");
        Console.WriteLine(Regex.Split("a😀b", "😀").Length == 2);
        Console.WriteLine(Regex.Split("a😀b", "😀")[0] == "a");
        Console.WriteLine(Regex.Split("a😀b", "😀")[1] == "b");
        Console.WriteLine(Regex.Split("😀x😀", "😀").Length == 3);
        Console.WriteLine(Regex.Split("😀x😀", "😀")[1] == "x");
        Console.WriteLine(Regex.IsMatch("abc😀def", "^abc.*def$") == true);
        Console.WriteLine(Regex.IsMatch("中😀中", "中😀中") == true);

        // ============ 8. empty-input edges ============
        Console.WriteLine(Regex.IsMatch("", "中") == false);
        Console.WriteLine(Regex.Replace("", "中", "X") == "");
        Console.WriteLine(Regex.Split("", "中").Length == 1);

        // ============ 9. \p{...} Unicode categories (D-REG-UNI-1) ============
        // The runtime category tables were generated 2026-08-12 from a net48
        // probe over every BMP code point; these assertions are the probe's
        // exact output for single-UTF-16-unit input. Categories supported:
        // \p{L} \p{N} \p{P} \p{Lu} \p{Ll}. Other categories (M S Z C,
        // Is* blocks) stay out of subset (parse error -> ArgumentException).
        // \p{L}: letters
        Console.WriteLine(Regex.IsMatch("a", @"\p{L}") == true);
        Console.WriteLine(Regex.IsMatch("A", @"\p{L}") == true);
        Console.WriteLine(Regex.IsMatch("中", @"\p{L}") == true);
        Console.WriteLine(Regex.IsMatch("龥", @"\p{L}") == true);
        Console.WriteLine(Regex.IsMatch("あ", @"\p{L}") == true);
        Console.WriteLine(Regex.IsMatch("ア", @"\p{L}") == true);
        Console.WriteLine(Regex.IsMatch("한", @"\p{L}") == true);
        Console.WriteLine(Regex.IsMatch("α", @"\p{L}") == true);
        Console.WriteLine(Regex.IsMatch("1", @"\p{L}") == false);
        Console.WriteLine(Regex.IsMatch(" ", @"\p{L}") == false);
        Console.WriteLine(Regex.IsMatch("!", @"\p{L}") == false);
        // \p{N}: numbers
        Console.WriteLine(Regex.IsMatch("1", @"\p{N}") == true);
        Console.WriteLine(Regex.IsMatch("0", @"\p{N}") == true);
        Console.WriteLine(Regex.IsMatch("１", @"\p{N}") == true);
        Console.WriteLine(Regex.IsMatch("９", @"\p{N}") == true);
        Console.WriteLine(Regex.IsMatch("Ⅲ", @"\p{N}") == true);
        Console.WriteLine(Regex.IsMatch("a", @"\p{N}") == false);
        Console.WriteLine(Regex.IsMatch("中", @"\p{N}") == false);
        Console.WriteLine(Regex.IsMatch("!", @"\p{N}") == false);
        // \p{P}: punctuation
        Console.WriteLine(Regex.IsMatch("!", @"\p{P}") == true);
        Console.WriteLine(Regex.IsMatch(".", @"\p{P}") == true);
        Console.WriteLine(Regex.IsMatch(",", @"\p{P}") == true);
        Console.WriteLine(Regex.IsMatch("，", @"\p{P}") == true);
        Console.WriteLine(Regex.IsMatch("。", @"\p{P}") == true);
        Console.WriteLine(Regex.IsMatch("？", @"\p{P}") == true);
        Console.WriteLine(Regex.IsMatch("(", @"\p{P}") == true);
        Console.WriteLine(Regex.IsMatch("a", @"\p{P}") == false);
        Console.WriteLine(Regex.IsMatch("中", @"\p{P}") == false);
        Console.WriteLine(Regex.IsMatch("1", @"\p{P}") == false);
        // \p{Lu} / \p{Ll}: case-specific
        Console.WriteLine(Regex.IsMatch("A", @"\p{Lu}") == true);
        Console.WriteLine(Regex.IsMatch("Z", @"\p{Lu}") == true);
        Console.WriteLine(Regex.IsMatch("Α", @"\p{Lu}") == true);
        Console.WriteLine(Regex.IsMatch("a", @"\p{Lu}") == false);
        Console.WriteLine(Regex.IsMatch("中", @"\p{Lu}") == false);
        Console.WriteLine(Regex.IsMatch("a", @"\p{Ll}") == true);
        Console.WriteLine(Regex.IsMatch("α", @"\p{Ll}") == true);
        Console.WriteLine(Regex.IsMatch("A", @"\p{Ll}") == false);
        Console.WriteLine(Regex.IsMatch("中", @"\p{Ll}") == false);
        // \P{L}: complement
        Console.WriteLine(Regex.IsMatch("1", @"\P{L}") == true);
        Console.WriteLine(Regex.IsMatch("!", @"\P{L}") == true);
        Console.WriteLine(Regex.IsMatch("a", @"\P{L}") == false);
        Console.WriteLine(Regex.IsMatch("中", @"\P{L}") == false);
        // inside a class
        Console.WriteLine(Regex.IsMatch("a", @"[\p{L}]") == true);
        Console.WriteLine(Regex.IsMatch("中", @"[\p{L}]") == true);
        Console.WriteLine(Regex.IsMatch("1", @"[\p{L}]") == false);
        Console.WriteLine(Regex.IsMatch("1", @"[^\p{L}]") == true);
        Console.WriteLine(Regex.IsMatch("a", @"[^\p{L}]") == false);
        Console.WriteLine(Regex.IsMatch("１", @"[\p{N}]") == true);
        Console.WriteLine(Regex.IsMatch("a", @"[\p{N}]") == false);
        // quantifiers + anchors
        Console.WriteLine(Regex.IsMatch("abc中文", @"^\p{L}+$") == true);
        Console.WriteLine(Regex.IsMatch("abc123", @"^\p{L}+$") == false);
        Console.WriteLine(Regex.IsMatch("123", @"^\p{N}+$") == true);
        Console.WriteLine(Regex.IsMatch("12a", @"^\p{N}+$") == false);
        Console.WriteLine(Regex.IsMatch("!!!", @"^\p{P}+$") == true);
        Console.WriteLine(Regex.IsMatch("123-456", @"(\p{N}+)-(\p{N}+)") == true);
    }}}
