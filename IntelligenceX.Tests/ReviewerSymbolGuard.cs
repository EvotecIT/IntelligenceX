// This project uses a single-binary test harness plus conditional compilation to include reviewer-only tests.
// Make it a build-time error to accidentally ship non-NET472 builds without the reviewer symbol, because
// that would silently omit reviewer test coverage in CI.
#if !NET472 && !INTELLIGENCEX_REVIEWER
#error INTELLIGENCEX_REVIEWER must be defined for non-NET472 builds so reviewer tests are compiled and executed.
#endif

