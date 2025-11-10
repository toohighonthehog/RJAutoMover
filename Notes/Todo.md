# RJAutoMover - TODO List

## Completed ✓

- [x] Improve the upgrade process (i.e. if a version already exists)
- [x] Zero byte and logged files shouldn't be skipped
- [x] Recents clearing on service restart not happening
- [x] Most recent/in progress should be at the top of the recents list
- [x] The recents view window should have the scrollbar in place from the start (if not, the column headings get messed up)
- [x] We are getting multiple logs when a large file is being processed. We should get one at the start and one at the end.
- [x] Changes to config.yaml should cause the application to go into an error state
- [x] Default location for logs setting
- [x] Test with a named service account
- [x] Why does memory usage increase? Can we put a limit?
- [x] There does appear to be a problem with the tray not starting because it is incorrectly concluded the tray is already open.

## In Progress / To Do


## Notes

- Items marked with `t` at the beginning have been marked as completed
- Memory limit functionality has been implemented (MemoryLimitMb in config)
- Config change detection has been implemented (enters error mode on external changes)
