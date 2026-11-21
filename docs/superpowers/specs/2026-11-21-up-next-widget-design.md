# Plan 6b: "Up next" widget and a roomier 2×1 calendar (short design)

**Why (user, 2026-11-21):**
- Google Calendar works now, but the 2×1 calendar cuts long titles ("Guru Tegh Bahadur's M…").
- The user wants a widget that reads only their own plans: meetings, trips Gmail found, things they added. Holidays
  and birthdays don't belong there.

**What the user decided:**
- **Calendar only.** Gmail-found trips and bookings come through the "events from Gmail" Google already puts in the
  calendar. Gmail itself is never read, and no new permission is needed (user, 2026-11-21).
- "Go ahead" on the builder's picks below.

## Approaches
1. **Calendar only, filtered (picked).** Uses the existing read-only sign-in. Holidays are recognised by their
   calendar (`…#holiday@group.v.calendar.google.com`), birthdays by `eventType: birthday`.
2. **Also read Gmail.** This needs read access to all mail and a stronger Google warning, and the widget would have to
   guess which emails are plans. The user declined it.

## Builder's picks
**Which events are "yours"**
- Every event on the switched-on calendars, except:
  - holiday calendars (`#holiday@`);
  - the legacy contacts-birthday calendar (`#contacts@`);
  - `birthday` events;
  - `workingLocation` events.
- `fromGmail` events (flights, hotels, bookings) count as yours.

**"Up next" widget** (new kind `Agenda`, named "Up next" in ＋ Add; one per desktop)
- Sizes:
  - **2×2** (default): the next 4 events;
  - **2×1**: the next event, large;
  - **4×2**: the next 6 events in two columns.
- Each row:
  - **When**, in red when it is soon. One of "NOW", "IN 25 MIN", "IN 3 H", "TODAY 18:00", "TOMORROW 09:00", "TUE 24".
  - The title.
  - The place (`location`) when there is one, dimmed.
- A click on an event opens it in Google Calendar (`htmlLink`, https only) in the user's browser.
- With nothing coming up: "NOTHING PLANNED".
- Not signed in: "CONNECT GOOGLE CALENDAR IN SETTINGS", shown always, because the widget has nothing else to show.
- Redraws every minute while visible (the countdown); paused in full screen.

**2×1 calendar**
- The date column narrows to 2/5 of the width and the title drops to 13 px with up to 3 lines, so typical titles fit.
- Holidays still show there, because that widget is the date at a glance.

**Unchanged:** privacy rules (titles, places and links are never logged), refresh timing, and the encrypted cache.
Cached events from before this change have no place or link and count as yours until the next refresh, a few seconds
after start.
