// The tests of a project share one database and empty it: they run one after the other.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
