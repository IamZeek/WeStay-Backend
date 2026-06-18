// The background-job tests (BookingJobTests) trigger global batch jobs (auto-complete /
// auto-cancel) that mutate ALL eligible bookings, so tests must run serially — otherwise a job
// could change a booking another test is mid-assertion on.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
