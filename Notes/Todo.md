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
- [x] Update the .Net SDK to the latest version 10.
- [x] Make the colour of the pause/resume button the colour the icon/status will become if clicked (i.e. swap the colours)

## In Progress / To Do

- 0.9.6
- [x] Remove dark mode - we don't need it.
- [r] System tab should be renamed to status.
- [r] Runtime state (i.e. pause processing) is already in the transfers tab, so we can get rid of the runtime state tab.
- [r] Rename 'Job Config' tab to just 'Configuration'.
- [r] .Net and Version tabs can be combined into a single Version tab, with two buttons (similar to the way job config is formatted) for .Net
- [r] The version tab is still slow to appear - in addition, some other tabs can cause the application to pause/hang in an inconsistent way.  It is important the tabs are reponsive, so first change the view, then populate the data as it becomes available.
- [r] Add a clear history button to the history tab.  This shoud be a small icon to keep everything aligned.  If 'All Sessions' is selected, wipe all historic sessions (i.e. current sessions and historic sessions from the database).  If 'Previous Sessions' is selected, just clear the historic sessions (i.e. in the database).  If 'Current Session' is selected, grey out (dim) the clear history button.
- [r] Update packages (where appropriate - I think sqlite has an issue)
- [r] We get the status icon in the tray briefly flickering red for no obvious reason.  For diagnostic purproses, log all events which cause the icon to change where this isn't done already.
- [r] The text in dropdowns in Logs and Transfer don't appear to be vertically centered very well.
- [r] Depending on the application state, the usually blue band at the top should change colour to reflect the current state.  Make sure the background text is appropriately contrasted.
- [r] Make the pause/resume processing button look a bit prettier.  It should be moved up a few pixels, be the same colour as the status band, and be the same width regardless of the text.
- [r] transfers and logs should discretely show an explanation of their purging/cleaning rules.  please try not to take up too much space.  for transfers, this can be a tooltip on the 'clear transfer history' button.  for logs, add a 'Clear Logs' button with the same wastepaper bin icon and similarly positioned to the transfers tab.  Also, the purging/cleaning explanation should be a tooltip of this new button.
- [r] Remove the words 'Recent Transfers' from the transfers tab.  It's obvious.
- [r] Make the top of the logs tab look similar to the recent transfers tab, i.e. the two drop downs, then the waste paper bin icon on the right to clear logs.
- [r] if the application goes into an errored state (for example, the config yaml file has changed), the explanation should go in the error tab, not the tray icon. The tray icon status should just show a short message, such as 'Status: Config Changed'.
- [ ] Test the rules processing.

- 0.9.7
- [ ] Incorporate config editor into app, so, currently, we have two buttons in the job config tab (passed view, raw yaml), add an 'edit config' button.  I don't think we need to config executable any more.

- [ ] Fix the sqlite update problem (rojan:Script/Wacatac.B!ml)
- [ ] Test the rules processing.

## Ongoing / Maintenance

- [ ] Please clean any unused code and functions (classes, comments, variables etc.).  Also, please ensure readmes, mds and comments are up to date and match the codes current functionality and usage.  Also check all tooltips in the configurator match the documenation and design.


## Notes

- Items marked with `r` are coded but awaiting testing.
- Items marked with `f` are coded but still need work.
- Items marked with `x` at the beginning have been marked as completed.

