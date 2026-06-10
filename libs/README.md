# Drop-in libraries

**No drop-ins are currently required.**

Originally this folder was meant for a standalone `Steamworks.NET.dll`. M0
recon showed My Winter Car embeds Steamworks.NET (classic API, with
`CSteamworks.dll` as the native bridge) inside
`mywintercar_Data/Managed/Assembly-CSharp-firstpass.dll`.

`WinterMP.Core` references that assembly directly (via `MwcGamePath` in
`Directory.Build.props.user`), which:

- compiles the Steam transport in automatically (`STEAMWORKS` define),
- shares the game's Steam callback dispatcher and AppID session at runtime,
- means we never ship or load a second Steamworks wrapper.

The folder is kept for future native/managed drop-ins (e.g. compression libs).
