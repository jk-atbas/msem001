// Fail a hanging test fast instead of blocking the whole suite (e.g. a forgotten FakeTimeProvider.Advance).
[assembly: Timeout(30_000)]
