# Third-Party Notices

OrbMet bundles provider marks derived from [Lobe Icons](https://github.com/lobehub/lobe-icons), taken from the copies in [Pulse](https://github.com/qunqin24/Pulse):

- `src/OrbMet.Desktop/Assets/Marks/claude.svg`
- `src/OrbMet.Desktop/Assets/Marks/openai.svg`

Lobe Icons is distributed under the MIT License:

```text
MIT License

Copyright (c) 2023 LobeHub

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

The Claude and OpenAI names and marks remain the property of their respective
owners. Their inclusion identifies the services OrbMet reads usage from and
does not imply endorsement.

## Pulse

OrbMet's panel is inspired by [Pulse](https://github.com/qunqin24/Pulse), a
macOS usage monitor distributed under the Apache License 2.0 (full text in
`licenses/Pulse-LICENSE.txt`). The ring and spacing measurements, the
usage-only colour scale and the interaction rules (instant hover, click a ring
to refresh) follow Pulse's.

Earlier OrbMet revisions contained two shapes adapted from Pulse's source:
`DockBerth.cs` (from `DockBerthShape` in `Sources/Pulse/Panel/UsageDockView.swift`)
and `DetailBubble.cs` (from `UsageBubbleShape` in
`Sources/Pulse/Panel/UsageBubbleShape.swift`), both ported from SwiftUI to
WPF. They were replaced by OrbMet's own capsule strip and tailless card and
are no longer in the source tree; this notice and the licence copy remain for
the revisions that include them.
