# 0014. Photos are named after their AI description

- Status: Accepted (2026-11-21)

## Context

Photos arrive with camera names (`IMG_1234.jpg`, `PXL_20240704_182201.jpg`) that say nothing about them, and
those names follow the photos into every download. protect.ps1 could rename photos with a local model before
encrypting them, which is why many legacy names are descriptive (`woman_and_boy_smiling_on_beach.jpg`). The app
already asks Gemini for a short description of each photo, so the name can come from that description.

## Decision

- **Every AI description renames the photo**: *Describe with AI* on a photo, descriptions made during upload, and
  **Generate missing descriptions** on the Actions page. A description written or edited by a person never
  renames a photo, and photos described before this change keep their names.
- **The name is the description in underscores**: its words in lowercase joined by `_`, shortened at a word to at
  most 60 characters (dropping a dangling "a", "on", "the"...), plus the lowercased extension of the current
  name: `dog_running_in_the_snow.jpg`. This matches the names protect.ps1 produced. A description is untrusted
  model output, so only letters and digits survive; anything else separates words. A name that would be a Windows
  device name (`con`, `lpt1`...) gets `_photo` appended. The rule is `FileNames.FromDescription`, and
  `Photo.SetAiDescription` applies it wherever an AI description is stored.
- **The name the photo arrived with is kept.** `photos.original_file_name` (the uploaded name, or the name in a
  legacy object key) never changes, stays searchable and is shown in the photo's details as "Original name".
  `photos.file_name` is the name the library shows and gives to downloads (single photos and ZIP entries).
- **The archive is untouched.** v2 files keep the uploaded name in their sealed metadata, and v2 object keys
  never contained a name. A database rebuilt from the archive names each photo after its sealed AI description,
  exactly as ingest did. Legacy objects keep their keys.
- **Only workers rename.** Workers write AI descriptions, so they write the new name. The web tier's database
  role cannot change either name column.

## Consequences

- Similar photos get the same name (a burst of shots of one moment). Names need not be unique: a ZIP numbers
  repeats as `name (2).jpg`, and browsers do the same for single downloads.
- A wrong AI description gives a wrong name. Fixing the description by hand leaves the name as it is (the name
  would otherwise change whenever a typo is fixed); *Describe with AI* replaces both.
- Photos described before this change keep their names. They can be renamed by *Describe with AI*, or all at
  once by a future one-off step that applies the same rule to stored descriptions (no Gemini calls needed).
- The previous release, which does not know `file_name`, keeps serving during a rollout and comes back on a
  rollback. So that the photos it inserts get the name they arrived with (instead of failing), the migration adds
  a `BEFORE INSERT` trigger that fills a missing `file_name` from `original_file_name`. This release always sets
  the name, so the trigger changes nothing for it; it can be dropped once no older release can run.
