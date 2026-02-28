using Xunit;

// Disable parallel execution of test classes. 
// This helps identify if hangs are caused by shared state/resource contention.
// Many tests interact with static ThreadHelper or shared filesystem paths.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
