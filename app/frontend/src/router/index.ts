import { nextTick } from 'vue'
import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

/**
 * Routes are code-split (dynamic imports) so each view's code loads on first visit. The photo viewer is a
 * nested route under the gallery, which makes individual photos deep-linkable (/gallery/photos/:id).
 * Guards here are for user experience only; every API call is authorized server-side.
 */
export const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    { path: '/', redirect: { name: 'gallery' } },
    {
      path: '/login',
      name: 'login',
      component: () => import('@/features/auth/LoginView.vue'),
      meta: { public: true, title: 'Sign in' },
    },
    {
      // Invite links (/invite#<token>) for family members; the token never leaves the page except in one POST.
      path: '/invite',
      name: 'invite',
      component: () => import('@/features/auth/InviteView.vue'),
      meta: { public: true, title: 'Invitation' },
    },
    {
      path: '/gallery',
      name: 'gallery',
      component: () => import('@/features/gallery/GalleryView.vue'),
      meta: { title: 'Gallery', breadcrumb: [{ label: 'Gallery' }] },
      children: [
        {
          path: 'photos/:id',
          name: 'photo',
          component: () => import('@/features/gallery/PhotoViewer.vue'),
          props: true,
          meta: {
            title: 'Photo',
            breadcrumb: [{ label: 'Gallery', to: { name: 'gallery' } }, { label: 'Photo' }],
          },
        },
      ],
    },
    {
      path: '/uploads',
      name: 'uploads',
      component: () => import('@/features/uploads/UploadsView.vue'),
      meta: { title: 'Uploads', breadcrumb: [{ label: 'Uploads' }] },
    },
    {
      path: '/actions',
      name: 'actions',
      component: () => import('@/features/jobs/ActionsView.vue'),
      meta: { title: 'Actions', requiresAdmin: true, breadcrumb: [{ label: 'Actions' }] },
    },
    {
      path: '/people',
      name: 'people',
      component: () => import('@/features/people/PeopleView.vue'),
      meta: { title: 'People', requiresAdmin: true, breadcrumb: [{ label: 'People' }] },
    },
    {
      path: '/:pathMatch(.*)*',
      name: 'not-found',
      component: () => import('@/features/errors/NotFoundView.vue'),
      meta: { public: true, title: 'Not found' },
    },
  ],
  scrollBehavior: (to, from, saved) =>
    to.name === 'photo' || from.name === 'photo' ? false : (saved ?? { top: 0 }),
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  await auth.ensureLoaded()
  if (!auth.loaded) {
    // The session could not be checked (offline, server error): that says nothing about being signed in. Public
    // pages still render; others go to the sign-in page, which explains it and offers to try again. The session
    // stays unchecked, so the next navigation asks the server again.
    return to.meta.public
      ? true
      : { name: 'login', query: { returnUrl: to.fullPath, error: 'unavailable' } }
  }

  if (!to.meta.public && !auth.isAuthenticated) {
    return { name: 'login', query: { returnUrl: to.fullPath } }
  }

  if (to.name === 'login' && auth.isAuthenticated) {
    // Already signed in (for example the session check worked on a second try): go where the user was headed.
    // Only local paths are followed, never another site.
    const returnUrl = to.query.returnUrl
    return typeof returnUrl === 'string' && returnUrl.startsWith('/') && !returnUrl.startsWith('//')
      ? returnUrl
      : { name: 'gallery' }
  }

  if (to.meta.requiresAdmin && !auth.isAdmin) {
    return { name: 'gallery' }
  }

  return true
})

router.afterEach(async (to, from) => {
  document.title = to.meta.title ? `${to.meta.title} · Photo Manager` : 'Photo Manager'

  // Move focus to the new page's heading so screen-reader and keyboard users land on the new content
  // (opening/closing the photo viewer manages focus itself). Not when only the query or hash changed on the
  // same page: the gallery search writes ?q= as you type, and focus must stay in the search box.
  if (
    from.matched.length > 0 &&
    to.path !== from.path &&
    to.name !== 'photo' &&
    from.name !== 'photo'
  ) {
    await nextTick()
    document.querySelector<HTMLElement>('[data-page-title]')?.focus()
  }
})
