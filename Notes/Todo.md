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
- [x] Change the order of the tabs to Transfers > Config > Logs > System > Version

## In Progress / To Do

- [r] Remove dark mode - we don't need it.
- [r] System tab should be renamed to status.
- [r] Runtime state (i.e. pause processing) is already in the transfers tab, so we can get rid of the runtime state tab.
- [r] Rename 'Job Config' tab to just 'Configuration'.
- [r] .Net and Version tabs can be combined into a single Version tab, with two buttons (similar to the way job config is formatted) for .Net
- [f] Version tab slow to appear.
- [r] Add a clear history button to the history tab.
- [ ] Update packages (where appropriate - I think sqlite has an issue)
- [x] Update the .Net SDK to the latest version 10.
- [ ] We get the status icon in the tray briefly flickering red for no obvious reason.
- [r] The text in dropdowns in Logs and Transfer don't appear to be vertically centered very well.
- [ ] Depending on the application state, the usually blue band at the top should change colour to reflect the current state.  Make sure the background text is appropriately contrasted.
- [ ] Incorporate config editor into app, so, currently, we have two buttons in the job config tab (passed view, raw yaml), add an 'edit config' button.  I don't think we need to config executable any more.
- [ ] Test the rules processing.

## Notes

- Items marked with `r` are coded but awaiting testing.
- Items marked with `f` are coded but still need work.
- Items marked with `x` at the beginning have been marked as completed.
