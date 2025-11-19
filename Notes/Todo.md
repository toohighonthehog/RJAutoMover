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

- [ ] Remove dark mode - we don't need it.
- [ ] We get the status icon in the tray briefly flickering red for no obvious reason.
- [ ] Version tab slow to appear.
- [ ] The text in dropdowns in Logs and Transfer don't appear to be vertically centered very well.
- [ ] System tab should be renamed to status.
- [ ] Runtime state (i.e. pause processing) is already in the transfers tab, so we can get rid of the runtime state tab.
- [ ] Rename 'Job Config' tab to just 'Configuration'.
- [ ] .NET and Version tabs can be conbined into a single Version tab, with two buttons (similar to the way job config is formatted) for .NET
- [ ] Depending on the application state, the usually blue band at the top should change colour to reflect the current state.  Make sure the background text is appropriately contrasted.
- [ ] Change the order of the tabs to;
    		Transfers
    		Config
    		Logs
    		System
    		Version
- [ ] Incorporate config editor into app, so, currently, we have two buttons in the job config tab (passed view, raw yaml), add an 'edit config' button.  I don't think we need to config executable any more.
- [ ] Test the rules processing.

## Notes

- Items marked with `t` at the beginning have been marked as completed
- Memory limit functionality has been implemented (MemoryLimitMb in config)
- Config change detection has been implemented (enters error mode on external changes)
