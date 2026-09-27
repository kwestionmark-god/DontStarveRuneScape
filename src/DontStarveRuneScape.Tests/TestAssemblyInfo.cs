using Xunit;

// The suite touches static test seams (Settings.PathOverride); keep the run
// sequential so parallel classes cannot race on them.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
