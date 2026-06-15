# MX Read System Design

## System Overview

MX Read is built as a mixed reality companion app for Meta Quest headsets that uses QR markers to anchor digital annotations to physical book pages. The system combines computer vision, spatial input from Logitech MX Ink, audio recording, and page tracking so users can interact with a real book while seeing digital overlays in the headset.

## Core Components

- **QR Marker Tracking**
  - Each printed page includes a unique QR code.
  - The app detects the visible QR marker and uses it to identify the current page.
  - Page-linked annotations are loaded and displayed based on the detected marker.

- **Mixed Reality Renderer**
  - Renders 3D annotation content in the headset view.
  - Aligns digital highlights, ink strokes, and sticky notes with the book surface.
  - Supports seamless transitions between pages.

- **Pen Interaction**
  - Logitech MX Ink is mapped as a natural writing and selection tool.
  - Users can draw, highlight, and place virtual note objects in 3D.
  - Pen input is translated into spatial strokes using the current page anchor.

- **Voice Notes**
  - Voice recordings are attached to a specific page.
  - Playback is available when the same page marker is detected again.
  - This enables audio annotations that stay linked to the reading context.

## Data Flow

1. The headset camera scans the book area.
2. The QR marker is detected and decoded.
3. The app selects the matching page state.
4. User input is captured from the MX Ink device.
5. Ink strokes, highlights, notes, and voice data are stored with a page association.
6. When the page changes, the app updates content for the new marker.

## Installation Flow

- Install `MXRead.apk` to the Quest headset.
- Print the QR marker sheet and attach markers to book pages.
- Launch MX Read and enter mixed reality mode.
- Start using the pen to interact with the book.

## Deployment Notes

- The system is designed for headsets with passthrough mixed reality capabilities.
- QR markers must be printed clearly and placed consistently.
- The app should be granted camera and microphone permissions for page tracking and voice notes.

## Design Goals

- Enable fast, spatial note-taking with minimal interruption to reading.
- Keep annotations linked to physical page position using QR-based anchors.
- Support multimodal interaction: pen, voice, and spatial annotations.
- Deliver a smooth experience on Meta Quest devices using Logitech MX Ink.

## Future Enhancements

- Add real-time OCR to capture text snippets directly from pages.
- Improve page detection tolerance for angled or partially visible markers.
- Support cloud sync for annotations and voice notes.
- Add multi-user collaboration with shared page-linked notes.
