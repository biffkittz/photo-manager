import { admin, expect, expectAccessible, mockSession, test } from './fixtures'

test('uploads photos, shows progress to completion, and rejects unsupported files', async ({
  page,
}) => {
  await mockSession(page, admin)
  let polls = 0
  let csrfHeader: string | undefined
  await page.route('**/api/uploads?batchId=*', (route) => {
    if (route.request().method() === 'POST') {
      csrfHeader = route.request().headers()['x-csrf-token']
      return route.fulfill({ status: 202, json: { photoId: 'new-1', status: 'Queued' } })
    }

    polls++
    const ready = polls > 1
    return route.fulfill({
      json: [
        {
          photoId: 'new-1',
          fileName: 'beach.jpg',
          status: ready ? 'Ready' : 'Processing',
          statusDetail: null,
          description: ready ? 'children building a sandcastle' : null,
          descriptionState: ready ? 'Ready' : 'Pending',
          renditionState: ready ? 'Ready' : 'Pending',
          renditionVersion: 1,
          uploadedAt: '2024-07-04T12:00:00Z',
        },
      ],
    })
  })

  await page.goto('/uploads')
  await expectAccessible(page)

  await page.getByLabel('Choose photos').setInputFiles([
    {
      name: 'beach.jpg',
      mimeType: 'image/jpeg',
      buffer: Buffer.from([0xff, 0xd8, 0xff, 0xe0, 1, 2, 3]),
    },
    { name: 'notes.txt', mimeType: 'text/plain', buffer: Buffer.from('hello') },
  ])

  const queue = page.getByRole('list', { name: 'Upload queue' })
  await expect(queue.getByText('Rejected')).toBeVisible()
  await expect(queue.getByText('children building a sandcastle')).toBeVisible({ timeout: 15_000 })
  await expect(queue.getByText('Done')).toBeVisible()
  expect(csrfHeader).toBe('test-csrf')
  await expectAccessible(page)
})

test('uploads can skip the AI description, and the choice is remembered', async ({ page }) => {
  await mockSession(page, admin)
  const posted: string[] = []
  await page.route('**/api/uploads?batchId=*', (route) => {
    if (route.request().method() === 'POST') {
      posted.push(route.request().url())
      return route.fulfill({ status: 202, json: { photoId: 'quiet-1', status: 'Queued' } })
    }

    return route.fulfill({ json: [] })
  })

  await page.goto('/uploads')
  const describe = page.getByLabel('Describe photos with AI (Gemini)')
  await expect(describe).toBeChecked()
  await describe.uncheck()
  await expectAccessible(page)

  await page.getByLabel('Choose photos').setInputFiles([
    {
      name: 'quiet.jpg',
      mimeType: 'image/jpeg',
      buffer: Buffer.from([0xff, 0xd8, 0xff, 0xe0, 1, 2, 3]),
    },
  ])
  await expect.poll(() => posted.length).toBe(1)
  expect(new URL(posted[0]).searchParams.get('describe')).toBe('false')

  await page.reload()
  await expect(page.getByLabel('Describe photos with AI (Gemini)')).not.toBeChecked()
})
