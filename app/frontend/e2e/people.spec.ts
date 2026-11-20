import type { Page } from '@playwright/test'
import { admin, expect, expectAccessible, mockSession, test } from './fixtures'

const later = '2026-12-01T12:00:00Z'

const people = {
  googleSignIn: true,
  gitHubAccounts: [{ login: 'octo', role: 'Admin' }],
  members: [
    {
      id: -1,
      name: 'Grandma',
      status: 'Active',
      googleEmail: 'grandma@example.com',
      inviteEmail: null,
      invitedBy: 'octo',
      inviteExpiresAt: null,
      joinedAt: '2026-11-20T12:00:00Z',
      lastSignInAt: '2026-11-21T12:00:00Z',
      removedAt: null,
    },
    {
      id: -2,
      name: 'Uncle Bob',
      status: 'Expired',
      googleEmail: null,
      inviteEmail: null,
      invitedBy: 'octo',
      inviteExpiresAt: '2026-11-01T12:00:00Z',
      joinedAt: null,
      lastSignInAt: null,
      removedAt: null,
    },
  ],
}

async function mockPeople(page: Page): Promise<{ posts: { url: string; body: unknown }[] }> {
  const posts: { url: string; body: unknown }[] = []
  await page.route('**/api/members**', (route) => {
    const request = route.request()
    if (request.method() === 'GET') {
      return route.fulfill({ json: people })
    }

    posts.push({ url: new URL(request.url()).pathname, body: request.postDataJSON() as unknown })
    if (request.url().endsWith('/remove')) {
      return route.fulfill({ json: { ...people.members[0], status: 'Removed' } })
    }

    const body = request.postDataJSON() as { name?: string; email?: string | null } | null
    return route.fulfill({
      json: {
        token: 'tok_EN-secret',
        expiresAt: later,
        member: {
          ...people.members[1],
          id: body?.name ? -3 : -2,
          name: body?.name ?? 'Uncle Bob',
          status: 'Invited',
          inviteEmail: body?.email ?? null,
          inviteExpiresAt: later,
        },
      },
    })
  })
  return { posts }
}

test('admins invite family members and get a one-time link to send', async ({ page }) => {
  await mockSession(page, admin, { googleSignIn: true })
  const { posts } = await mockPeople(page)
  await page.goto('/people')

  await expect(page.getByRole('heading', { name: 'People', level: 1 })).toBeVisible()
  await expect(page.getByText('Google account grandma@example.com')).toBeVisible()
  await expect(page.getByText('octo', { exact: true })).toBeVisible()
  await expectAccessible(page)

  // Validation happens before anything is sent.
  await page.getByLabel('Google account email (optional)').fill('not an email')
  await page.getByRole('button', { name: 'Create invite link' }).click()
  await expect(page.getByText('Enter a name.')).toBeVisible()
  await expect(page.getByText('Enter an email address like name@gmail.com')).toBeVisible()
  expect(posts).toEqual([])

  await page.getByLabel('Name').fill('Aunt May')
  await page.getByLabel('Google account email (optional)').fill('may@example.com')
  await page.getByRole('button', { name: 'Create invite link' }).click()

  await expect(page.getByLabel('Invite link')).toHaveValue(/\/invite#tok_EN-secret$/)
  await expect(
    page.getByText('Only the Google account may@example.com can accept it.'),
  ).toBeVisible()
  expect(posts).toEqual([
    { url: '/api/members', body: { name: 'Aunt May', email: 'may@example.com' } },
  ])
  await expectAccessible(page)

  // A new link for an expired invite.
  await page.getByRole('button', { name: 'New link: Uncle Bob' }).click()
  await expect(page.getByRole('heading', { name: 'Invite link for Uncle Bob' })).toBeVisible()
  expect(posts.at(-1)?.url).toBe('/api/members/-2/invite')
})

test('removing someone asks for confirmation first', async ({ page }) => {
  await mockSession(page, admin, { googleSignIn: true })
  const { posts } = await mockPeople(page)
  await page.goto('/people')

  await page.getByRole('button', { name: 'Remove: Grandma' }).click()
  const dialog = page.getByRole('dialog', { name: 'Remove access' })
  await expect(dialog).toContainText('signed out at once')
  await expectAccessible(page)
  expect(posts).toEqual([])

  await dialog.getByRole('button', { name: 'Remove' }).click()
  await expect(dialog).toBeHidden()
  expect(posts).toEqual([{ url: '/api/members/-1/remove', body: null }])
})

test('without Google sign-in configured, inviting is switched off and explained', async ({
  page,
}) => {
  await mockSession(page, admin)
  await page.route('**/api/members', (route) =>
    route.fulfill({ json: { ...people, googleSignIn: false, members: [] } }),
  )
  await page.goto('/people')

  await expect(page.getByRole('status')).toContainText('Google sign-in is not set up yet')
  await expect(page.getByRole('button', { name: 'Create invite link' })).toBeDisabled()
  await expectAccessible(page)
})

test('an invite link leads to Google sign-in without leaving its token in the address bar', async ({
  page,
}) => {
  await mockSession(page, null, { googleSignIn: true })
  let redeemed: { token?: string; csrf?: string } = {}
  await page.route('**/api/auth/invites/redeem', (route) => {
    redeemed = {
      token: (route.request().postDataJSON() as { token: string }).token,
      csrf: route.request().headers()['x-csrf-token'],
    }
    return route.fulfill({
      json: { name: 'Grandma', invitedBy: 'octo', expiresAt: later, forSpecificAccount: false },
    })
  })

  await page.goto('/invite#tok_EN-secret')

  await expect(page.getByText('octo invited Grandma to the family photo library.')).toBeVisible()
  expect(redeemed).toEqual({ token: 'tok_EN-secret', csrf: 'anonymous-csrf' })
  await expect(page).toHaveURL(/\/invite$/)
  await expect(page.getByRole('link', { name: 'Continue with Google' })).toHaveAttribute(
    'href',
    '/api/auth/login/google?returnUrl=%2Fgallery',
  )
  await expectAccessible(page)
})

test('an invite link that no longer works is explained', async ({ page }) => {
  await mockSession(page, null, { googleSignIn: true })
  await page.route('**/api/auth/invites/redeem', (route) =>
    route.fulfill({ status: 404, json: { title: 'This invite link does not work.' } }),
  )
  await page.goto('/invite#tok_used')

  await expect(page.getByRole('alert')).toContainText('Ask the person who invited you')
  await expect(page.getByRole('link', { name: 'Continue with Google' })).toHaveCount(0)
  await expectAccessible(page)
})

test('members cannot open the People page', async ({ page }) => {
  await mockSession(page, { ...admin, role: 'Member' })
  await page.route('**/api/photos**', (route) =>
    route.fulfill({ json: { items: [], nextCursor: null } }),
  )
  await page.goto('/people')

  await expect(page).toHaveURL(/\/gallery$/)
  await expect(page.getByRole('link', { name: 'People' })).toHaveCount(0)
})
