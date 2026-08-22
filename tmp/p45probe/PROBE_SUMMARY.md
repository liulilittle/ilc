# Probe results (tmp/p45probe, net48 oracle, 3x byte-identical)

## Probe 1: TCM_SETITEMSIZE return = previous size (LOWORD/HIWORD) -- CONFIRMED
p45_step1_old_w=96  p45_step1_old_h=0    (first call returns font-metric default -> DISCARD)
p45_prev_w=60       p45_prev_h=24        (SetItemSize(80,28) returns previous explicit 60/24)
p45_prev2_w=80      p45_prev2_h=28       (SetItemSize(50,20) returns previous explicit 80/28)
Two-step method: first call's return discarded, subsequent calls return the LAST EXPLICIT value.

## Probe 2: TCS_FIXEDWIDTH necessity -- CONFIRMED
Plain control (no TCS_FIXEDWIDTH): rect_before w=54, SetItemSize(60,24) -> rect_after w=54 (UNCHANGED).
Without TCS_FIXEDWIDTH/TCS_OWNERDRAWFIXED the item size is IGNORED (rect stays text-width ~54).
With TCS_FIXEDWIDTH + SetItemSize(50,20): rect0 w=50 exactly.

## Probe 3: Multiline rows = f(client width, item width) -- probe-finalized
Item 50x20, 3 tabs (Tab0/Tab1/Tab2):
  client 90..104  -> 3 rows  (1 tab/row)
  client 105..154 -> 2 rows  (2 tabs on row 1, 1 on row 2)
  client 155+     -> 1 row   (all 3 fit)
Naive guess 100/50=2 is WRONG: at 100px it's 3 rows (a ~5px comctl32 margin). Use width 110 -> 2 rows (mid-band, deterministic).

## Probe 4: fixed-width non-MULTILINE GETROWCOUNT -- CONFIRMED
p45_rows1=1 (TCS_FIXEDWIDTH alone, 320 wide, 3 tabs -> 1 row).

## Misc
client width == window width for the child (no border). RectBool after SetItemSize(50,20):
w=50;lr=True;tb=True;ne=True;pos=True.
ID_TAB45=0x2B0=688 follows ID_LIST43=0x2A0=672.
