import type { Locator, Page } from '@playwright/test'
import { admin, expect, expectAccessible, mockSession, pixelPng, test } from './fixtures'

// A phone: 390 x 844 CSS pixels with a touch screen (so `pointer: coarse` styles apply).
test.use({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true })

/** A portrait "photo" (3:4) as large as a real preview, so layout bugs with tall images show up. */
const portraitPreview = `<svg xmlns="http://www.w3.org/2000/svg" width="1500" height="2000"><rect width="1500" height="2000" fill="#16a34a"/></svg>`

const longDescription =
  'Three generations of the family gathered around a long wooden picnic table under the old oak tree, with ' +
  'grandma cutting a strawberry birthday cake while the children lean in and the dog waits for crumbs.'

const photos = Array.from({ length: 12 }, (_, i) => ({
  id: `p${i + 1}`,
  fileName: `IMG_${i + 1}.jpg`,
  description: i === 0 ? longDescription : `photo number ${i + 1}`,
  uploadedAt: '2024-07-04T12:00:00Z',
  takenAt: null,
  width: 3000,
  height: 4000,
  renditionState: 'Ready',
  renditionVersion: 1,
}))

async function mockGallery(page: Page): Promise<void> {
  await mockSession(page, admin)
  await page.route('**/api/photos?*', (route) =>
    route.fulfill({ json: { items: photos, nextCursor: null } }),
  )
  await page.route(/\/api\/photos\/p\d+\/thumbnail/, (route) =>
    route.fulfill({ body: pixelPng, contentType: 'image/png' }),
  )
  await page.route(/\/api\/photos\/p\d+\/preview/, (route) =>
    route.fulfill({ body: portraitPreview, contentType: 'image/svg+xml' }),
  )
  await page.route(/\/api\/photos\/p\d+$/, (route) => {
    const photo = photos.find((p) => route.request().url().endsWith(`/${p.id}`))!
    return route.fulfill({
      json: {
        ...photo,
        descriptionSource: 'Gemini',
        descriptionState: 'Ready',
        tags: ['family', 'garden'],
        contentType: 'image/jpeg',
        sizeBytes: 4_812_345,
        uploadedBy: 'octo',
        status: 'Ready',
        statusDetail: null,
        encryptionFormat: 'V2',
        source: 'Upload',
      },
    })
  })
}

/** A one-finger swipe on `target` by (dx, dy) pixels, as touch events. */
async function swipe(target: Locator, dx: number, dy: number): Promise<void> {
  await target.evaluate(
    (element, [dx, dy]) => {
      const at = (x: number, y: number) =>
        new Touch({ identifier: 1, target: element, clientX: x, clientY: y })
      const start = at(200, 300)
      const end = at(200 + dx, 300 + dy)
      element.dispatchEvent(
        new TouchEvent('touchstart', { bubbles: true, touches: [start], changedTouches: [start] }),
      )
      element.dispatchEvent(
        new TouchEvent('touchend', { bubbles: true, touches: [], changedTouches: [end] }),
      )
    },
    [dx, dy],
  )
}

async function expectTappable(control: Locator): Promise<void> {
  const box = (await control.boundingBox())!
  expect(
    box.height,
    await control.evaluate((e) => e.outerHTML.slice(0, 80)),
  ).toBeGreaterThanOrEqual(44)
  expect(box.width).toBeGreaterThanOrEqual(44)
}

test('the photo viewer shows the whole photo above its details, and swipes between photos', async ({
  page,
}) => {
  await mockGallery(page)
  await page.goto('/gallery/photos/p1')

  const dialog = page.getByRole('dialog')
  const photo = dialog.getByRole('img', { name: longDescription })
  await expect(photo).toBeVisible()
  await expect.poll(() => photo.evaluate((img: HTMLImageElement) => img.complete)).toBe(true)

  // The photo stays inside its frame, and the details start below it (a tall photo used to cover them).
  const frame = (await photo.locator('..').boundingBox())!
  const image = (await photo.boundingBox())!
  const details = (await dialog
    .getByRole('complementary', { name: 'Photo details' })
    .boundingBox())!
  expect(image.y).toBeGreaterThanOrEqual(frame.y - 1)
  expect(image.y + image.height).toBeLessThanOrEqual(frame.y + frame.height + 1)
  expect(frame.y + frame.height).toBeLessThanOrEqual(details.y)
  // Full screen on a phone: the photo uses the whole width.
  expect(image.width).toBeGreaterThan(380)

  for (const control of [
    dialog.getByRole('button', { name: 'Close dialog' }),
    dialog.getByRole('button', { name: 'Next' }),
    dialog.getByRole('button', { name: 'Edit', exact: true }),
  ]) {
    await expectTappable(control)
  }
  await expectAccessible(page)

  await swipe(photo, -150, 10)
  await expect(page).toHaveURL(/\/gallery\/photos\/p2$/)
  await swipe(dialog.getByRole('img', { name: 'photo number 2' }), 150, -10)
  await expect(page).toHaveURL(/\/gallery\/photos\/p1$/)

  // Scrolling down through the details is not a swipe.
  await swipe(photo, -60, -200)
  await expect(page).toHaveURL(/\/gallery\/photos\/p1$/)
})

test('selected photos can be downloaded from a bar that stays at the bottom of the screen', async ({
  page,
}) => {
  await mockGallery(page)
  await page.goto('/gallery')

  // The whole label is the tap area around the small checkbox.
  const select = page
    .getByLabel('Select photo number 2', { exact: true })
    .locator('xpath=ancestor::label[1]')
  await expectTappable(select)
  await expectTappable(
    page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Uploads' }),
  )
  await select.click()

  await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight))
  const bar = page.getByRole('region', { name: 'Selection' })
  await expect(bar).toBeInViewport()
  const box = (await bar.boundingBox())!
  expect(box.y + box.height).toBeGreaterThan(840)
  await expect(bar.getByRole('button', { name: 'Download original' })).toBeVisible()
  await expectAccessible(page)
})

test('no page is wider than the phone screen', async ({ page }) => {
  await mockGallery(page)
  await page.route('**/api/jobs/overview', (route) =>
    route.fulfill({
      json: {
        totalPhotos: 48213,
        missingRenditions: 1204,
        missingDescriptions: 15873,
        failedUploads: 3,
        missingFromArchive: 0,
      },
    }),
  )
  await page.route('**/api/jobs', (route) =>
    route.fulfill({
      json: [
        {
          id: 'j1',
          kind: 'BackfillDescriptions',
          status: 'Running',
          requestedBy: 'octo',
          createdAt: '2026-11-20T10:00:00Z',
          startedAt: '2026-11-20T10:00:05Z',
          completedAt: null,
          processed: 12345,
          succeeded: 12300,
          failed: 45,
          summary: 'Gemini quota reached, waiting 60 seconds before continuing',
        },
      ],
    }),
  )
  await page.route('**/api/members', (route) =>
    route.fulfill({
      json: {
        googleSignIn: true,
        gitHubAccounts: [{ login: 'octo', role: 'Admin' }],
        members: [
          {
            id: -1,
            name: 'Grandma',
            status: 'Active',
            googleEmail: 'grandma.with.a.long.address@example.com',
            inviteEmail: null,
            invitedBy: 'octo',
            inviteExpiresAt: null,
            joinedAt: '2026-11-20T12:00:00Z',
            lastSignInAt: '2026-11-21T12:00:00Z',
            removedAt: null,
          },
        ],
      },
    }),
  )

  const pages: [string, (page: Page) => Locator][] = [
    ['/gallery', (p) => p.getByRole('list', { name: 'Photos' })],
    ['/uploads', (p) => p.getByText('Choose photos')],
    ['/people', (p) => p.getByText('grandma.with.a.long.address@example.com')],
    // A six-column table used to make this page scroll sideways; phones get one card per job.
    ['/actions', (p) => p.getByRole('list', { name: 'Recent maintenance jobs' })],
  ]
  for (const [path, content] of pages) {
    await page.goto(path)
    await expect(content(page)).toBeVisible()
    await expectAccessible(page)
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    )
    expect(overflow, `${path} scrolls sideways`).toBeLessThanOrEqual(0)
  }

  // Still on /actions (last above).
  await expectTappable(page.getByRole('button', { name: 'Cancel Generate descriptions' }))
})
