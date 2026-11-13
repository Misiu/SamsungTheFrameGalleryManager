// TvLog is a process-wide sink: LogTests attaches a file to it, and any test that logs meanwhile (the view model tests do) writes into
// that file while it is being read. The whole suite takes a fraction of a second, so the tests simply run one after another.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
