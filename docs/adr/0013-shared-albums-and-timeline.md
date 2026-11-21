# 0013. Shared family albums, a year/month timeline and "On this day"

- Status: Accepted (2026-11-20)

## Context

Once the re-index imports the archive, the library is thousands of photos in one grid with keyword search. There
was no way to group a trip or a holiday, to find "summer 2015", or to show someone a person's photos; downloading
an event meant picking up to 500 photos by hand. The family uses the app together, including members who are not
technical (ADR 0012), so the design had to stay simple.

## Decision

- **Albums are shared by the whole family.** Everyone who can sign in sees every album and can create albums, add
  and remove photos, rename them, choose the cover and delete them, as with descriptions today. There are no
  per-album permissions or private albums. The album records who created it (`created_by_id`); the audit trail
  records every change (`album.create`, `album.update`, `album.add`, `album.remove`, `album.delete`).
- **Nothing is deleted.** Deleting an album sets `albums.deleted_at`, and removing a photo from an album sets
  `album_photos.removed_at` (adding it again clears it). Photos are never touched. The web tier's database role
  gets `SELECT, INSERT` on both tables and `UPDATE` on only the columns that change (name, cover, `deleted_at`,
  `updated_at`; `added_at`, `removed_at`). Workers get no access to either table.
- **Only library photos count**: an album lists, counts and downloads `Ready` photos only, like the gallery. A
  photo hidden as a duplicate or missing from the archive drops out of its albums without being removed from them.
- **Albums read like the event**: oldest photo first. The cover is the chosen photo while it is in the album,
  otherwise the first photo; removing the chosen cover forgets it.
- **An album downloads as one ZIP named after it**, within the existing download limits (500 photos, 4 GB, the
  daily limit of ADR 0001). The API resolves the album to photo ids and stores the ZIP name with the request; the
  worker keeps that name and is otherwise unchanged. A bigger album is downloaded by selecting up to 500 of its
  photos.
- **The timeline filters; it does not jump.** Year buttons above the gallery, then the chosen year's months,
  narrow the gallery to that year or month (`/gallery?year=2019&month=7`: shareable, and Back undoes it), shown in
  the order the photos were taken, with a link to the next older period at the end. It works the same on phones and
  computers and combines with search. A scrubber that scrolls one continuous gallery was rejected: it needs
  loading backwards, and phones need a different control.
- **"On this day"** shows, above the gallery, one card per earlier year with photos taken on today's date. Only
  real capture dates (EXIF) count, never upload dates.
- **Dates follow the viewer's calendar.** The browser sends its UTC offset (minutes) and, for "On this day", its
  date. Capture times without a time zone are stored as if they were UTC, and the tiles print them in the
  browser's zone, so grouping by the same offset keeps the timeline and "On this day" in agreement with the dates
  printed on the tiles.
- No new external services, no change to encryption or the archive.

## Consequences

- Any family member can delete or reshape an album someone else made. Deleting asks for confirmation and an
  admin can restore an album from the database (operations.md, "Albums"); the audit trail shows who did it.
- The timeline counts and "On this day" each read every library photo's date; fine for tens of thousands of
  photos. An expression index can be added if the library grows much larger.
- "On this day" and month boundaries apply the viewer's current UTC offset to every photo, so under daylight
  saving time a photo taken within an hour of midnight can land on the neighbouring day. Photos whose camera time
  zone is unknown are shifted by the viewer's offset, exactly as the dates printed on the tiles are.
- Albums of more than 500 photos cannot be downloaded in one go.
