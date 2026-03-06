using Xunit;

// UI tests drive a real process — parallel execution causes fixtures to kill
// each other's processes and race on TIMEGUARD_TEST_DB. Run everything serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
