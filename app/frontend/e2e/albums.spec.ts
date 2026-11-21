import { expect, expectAccessible, test } from './fixtures'
import { mockLibrary, photo } from './library'

const photos = [
  photo('p1', 'sunset over the lake', '2019-07-04T18:00:00Z'),
  photo('p2', 'dog running in the snow', '2019-07-01T09:00:00Z'),
  photo('p3', 'kids building a sandcastle', '2019-07-09T11:00:00Z'),
]

test('photos selected in the gallery become an album', async ({ page }) => {
  await mockLibrary(page, photos)
  await page.goto('/gallery')

  await page.getByLabel('Select sunset over the lake', { exact: true }).check()
  await page.getByLabel('Select dog running in the snow', { exact: true }).check()
  const selection = page.getByRole('region', { name: 'Selection' })
  await selection.getByRole('button', { name: 'Add to album' }).click()

  const dialog = page.getByRole('dialog', { name: 'Add 2 photos to an album' })
  await expect(dialog).toBeVisible()
  await expectAccessible(page)
  await dialog.getByRole('button', { name: 'Create album' }).click()
  await expect(dialog.getByRole('alert')).toHaveText('Give the album a name.')
  await dialog.getByLabel('New album').fill('Lake trip')
  await dialog.getByRole('button', { name: 'Create album' }).click()
  await expect(page.getByText('Created “Lake trip” with 2 photos.')).toBeVisible()
  await expect(dialog).toBeHidden()
  await expect(selection).toBeHidden()

  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Albums' }).click()
  await expect(page.getByRole('link', { name: /Lake trip/ })).toContainText('2 photos')
  await expectAccessible(page)
  await page.getByRole('link', { name: /Lake trip/ }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Lake trip' })).toBeVisible()
  // Oldest first: an album reads like the event it holds.
  const tiles = page.getByRole('list', { name: 'Photos in this album' }).getByRole('listitem')
  await expect(tiles).toHaveCount(2)
  await expect(
    tiles.first().getByRole('button', { name: 'Open dog running in the snow' }),
  ).toBeVisible()
  await expect(
    tiles.last().getByRole('button', { name: 'Open sunset over the lake' }),
  ).toBeVisible()
  await expectAccessible(page)
})

test('an album is filled from its page, given a cover, renamed, downloaded, trimmed and deleted', async ({
  page,
}) => {
  const library = await mockLibrary(page, photos, [
    { id: 'a1', name: 'Beach', coverPhotoId: null, photoIds: ['p1', 'p2'] },
  ])
  await page.goto('/albums/a1')
  const grid = page.getByRole('list', { name: 'Photos in this album' })
  await expect(grid.getByRole('listitem')).toHaveCount(2)

  // "Add photos" opens the gallery as this album's picker.
  await page.getByRole('button', { name: 'Add photos' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Add photos to “Beach”' })).toBeFocused()
  await page.getByLabel('Select kids building a sandcastle', { exact: true }).check()
  await page
    .getByRole('region', { name: 'Selection' })
    .getByRole('button', { name: 'Add to album' })
    .click()
  await expect(page).toHaveURL(/\/albums\/a1$/)
  await expect(page.getByText('Added 1 photo to “Beach”.')).toBeVisible()
  await expect(grid.getByRole('listitem')).toHaveCount(3)

  // The cover is chosen in the viewer, which steps through the album.
  await grid.getByRole('button', { name: 'Open sunset over the lake' }).click()
  const viewer = page.getByRole('dialog', { name: 'sunset over the lake' })
  await viewer.getByRole('button', { name: 'Use as album cover' }).click()
  await expect(viewer.getByText("This is the album's cover.")).toBeVisible()
  expect(library.albums[0]?.coverPhotoId).toBe('p1')
  await viewer.getByRole('button', { name: 'Next' }).click()
  await expect(page.getByRole('dialog', { name: 'kids building a sandcastle' })).toBeVisible()
  await expect(page).toHaveURL(/\/albums\/a1\/photos\/p3$/)
  await page.keyboard.press('Escape')
  await expect(page).toHaveURL(/\/albums\/a1$/)

  await page.getByRole('button', { name: 'Rename' }).click()
  const rename = page.getByRole('dialog', { name: 'Rename album' })
  await expect(rename.getByLabel('Name')).toHaveValue('Beach')
  await rename.getByLabel('Name').fill('Beach 2019')
  await rename.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Beach 2019' })).toBeVisible()

  // The browser is handed a link named after the album. (Playwright cannot intercept the file request itself,
  // so `vite preview` logs a proxy error for it; only the name matters here.)
  const download = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Download', exact: true }).click()
  expect((await download).suggestedFilename()).toBe('Beach 2019.zip')
  expect(library.requests).toContainEqual({
    method: 'POST',
    path: '/api/downloads',
    body: { albumId: 'a1' },
  })

  await page.getByLabel('Select dog running in the snow', { exact: true }).check()
  await page
    .getByRole('region', { name: 'Selection' })
    .getByRole('button', { name: 'Remove from album' })
    .click()
  const confirmRemove = page.getByRole('dialog', { name: 'Remove from album' })
  await expect(confirmRemove).toContainText('Remove 1 photo from “Beach 2019”?')
  await expectAccessible(page)
  await confirmRemove.getByRole('button', { name: 'Remove' }).click()
  await expect(grid.getByRole('listitem')).toHaveCount(2)
  await expect(page.getByRole('region', { name: 'Selection' })).toBeHidden()

  await page.getByRole('button', { name: 'Delete', exact: true }).click()
  await page
    .getByRole('dialog', { name: 'Delete album' })
    .getByRole('button', { name: 'Delete album' })
    .click()
  await expect(page).toHaveURL(/\/albums$/)
  await expect(page.getByText('“Beach 2019” was deleted.', { exact: false })).toBeVisible()
  await expect(page.getByText('No albums yet')).toBeVisible()
})

test('a deleted album says so instead of failing', async ({ page }) => {
  await mockLibrary(page, photos)
  await page.goto('/albums/a9')
  await expect(page.getByText('Album not found')).toBeVisible()
  await page.getByRole('link', { name: 'All albums' }).click()
  await expect(page).toHaveURL(/\/albums$/)
})
