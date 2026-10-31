// suffixprobe.cpp -- TWO-ARM export fixture for the CharSet A/W probe-order
// measurement (tests/cases/probe-entrypoint-suffix).
//
// The system DLLs on this machine export no "only-A" name (audit section 2.2:
// only-A = 0), so the relative order of the net48 "A" and "W" candidates is
// unobservable there. This fixture exports BOTH arms of one entry point with
// DIFFERENT results, so whichever arm net48 picks becomes visible in the
// oracle's stdout: the differential harness then forces the transpiled runtime
// to print the same value (a real measurement, not an assumption).
extern "C" {

int ProbeA(void)
{
    return 11;
}

int ProbeW(void)
{
    return 22;
}

// Second pair for the CharSet.Unicode arm (only "W" is in that candidate list).
int Probe2A(void)
{
    return 31;
}

int Probe2W(void)
{
    return 32;
}

} // extern "C"
