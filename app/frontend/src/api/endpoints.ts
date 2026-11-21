import { apiFetch } from './http'
import type {
  AlbumSummary,
  DownloadResponse,
  GalleryPeriod,
  GallerySort,
  JobKind,
  InviteCreated,
  InvitePreview,
  JobResponse,
  LibraryOverview,
  Member,
  OnThisDayYear,
  PeopleResponse,
  PhotoDetails,
  PhotoPage,
  SessionResponse,
  TimelineMonth,
  UploadItemStatus,
} from './types'

/** Typed wrappers for every API endpoint the SPA uses. */
export const api = {
  session: (signal?: AbortSignal) => apiFetch<SessionResponse>('/api/auth/session', { signal }),
  logout: () => apiFetch<void>('/api/auth/logout', { method: 'POST' }),
  /** Opens an invite link: the token comes from the link's #fragment and is only ever sent in this body. */
  redeemInvite: (token: string) =>
    apiFetch<InvitePreview>('/api/auth/invites/redeem', { method: 'POST', body: { token } }),

  /** `offset` is the viewer's UTC offset in minutes: years and months are those of their calendar. */
  listPhotos: (
    params: {
      q?: string
      cursor?: string | null
      sort?: GallerySort
      limit?: number
      period?: GalleryPeriod | null
      offset?: number
    },
    signal?: AbortSignal,
  ) => {
    const query = new URLSearchParams()
    if (params.q) query.set('q', params.q)
    if (params.cursor) query.set('cursor', params.cursor)
    if (params.sort) query.set('sort', params.sort)
    if (params.limit) query.set('limit', String(params.limit))
    if (params.period) {
      query.set('year', String(params.period.year))
      if (params.period.month) query.set('month', String(params.period.month))
    }
    if (params.offset) query.set('offset', String(params.offset))
    return apiFetch<PhotoPage>(`/api/photos?${query.toString()}`, { signal })
  },
  timeline: (q: string, offset: number, signal?: AbortSignal) => {
    const query = new URLSearchParams({ offset: String(offset) })
    if (q) query.set('q', q)
    return apiFetch<TimelineMonth[]>(`/api/photos/timeline?${query.toString()}`, { signal })
  },
  /** `date` is the viewer's date (yyyy-MM-dd). */
  onThisDay: (date: string, offset: number, signal?: AbortSignal) =>
    apiFetch<OnThisDayYear[]>(
      `/api/photos/on-this-day?${new URLSearchParams({ date, offset: String(offset) }).toString()}`,
      { signal },
    ),
  photo: (id: string, signal?: AbortSignal) =>
    apiFetch<PhotoDetails>(`/api/photos/${encodeURIComponent(id)}`, { signal }),
  updateDescription: (id: string, description: string) =>
    apiFetch<PhotoDetails>(`/api/photos/${encodeURIComponent(id)}`, {
      method: 'PATCH',
      body: { description },
    }),
  regenerateDescription: (id: string) =>
    apiFetch<void>(`/api/photos/${encodeURIComponent(id)}/describe`, { method: 'POST' }),

  uploads: (batchId?: string, signal?: AbortSignal) =>
    apiFetch<UploadItemStatus[]>(
      `/api/uploads${batchId ? `?batchId=${encodeURIComponent(batchId)}` : ''}`,
      { signal },
    ),
  retryUpload: (id: string) =>
    apiFetch<void>(`/api/uploads/${encodeURIComponent(id)}/retry`, { method: 'POST' }),

  /** Selected photos, or a whole album (a ZIP named after it). */
  createDownload: (target: string[] | { albumId: string }) =>
    apiFetch<DownloadResponse>('/api/downloads', {
      method: 'POST',
      body: Array.isArray(target) ? { photoIds: target } : { albumId: target.albumId },
    }),
  download: (id: string, signal?: AbortSignal) =>
    apiFetch<DownloadResponse>(`/api/downloads/${encodeURIComponent(id)}`, { signal }),

  albums: (signal?: AbortSignal) => apiFetch<AlbumSummary[]>('/api/albums', { signal }),
  album: (id: string, signal?: AbortSignal) =>
    apiFetch<AlbumSummary>(`/api/albums/${encodeURIComponent(id)}`, { signal }),
  albumPhotos: (id: string, cursor: string | null, signal?: AbortSignal) =>
    apiFetch<PhotoPage>(
      `/api/albums/${encodeURIComponent(id)}/photos${cursor ? `?cursor=${encodeURIComponent(cursor)}` : ''}`,
      { signal },
    ),
  createAlbum: (name: string, photoIds: string[]) =>
    apiFetch<AlbumSummary>('/api/albums', { method: 'POST', body: { name, photoIds } }),
  updateAlbum: (id: string, changes: { name?: string; coverPhotoId?: string }) =>
    apiFetch<AlbumSummary>(`/api/albums/${encodeURIComponent(id)}`, {
      method: 'PATCH',
      body: changes,
    }),
  deleteAlbum: (id: string) =>
    apiFetch<void>(`/api/albums/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  addToAlbum: (id: string, photoIds: string[]) =>
    apiFetch<AlbumSummary>(`/api/albums/${encodeURIComponent(id)}/photos`, {
      method: 'POST',
      body: { photoIds },
    }),
  removeFromAlbum: (id: string, photoIds: string[]) =>
    apiFetch<AlbumSummary>(`/api/albums/${encodeURIComponent(id)}/photos/remove`, {
      method: 'POST',
      body: { photoIds },
    }),

  jobs: (signal?: AbortSignal) => apiFetch<JobResponse[]>('/api/jobs', { signal }),
  overview: (signal?: AbortSignal) => apiFetch<LibraryOverview>('/api/jobs/overview', { signal }),
  startJob: (kind: JobKind, force = false) =>
    apiFetch<JobResponse>('/api/jobs', { method: 'POST', body: { kind, force } }),
  cancelJob: (id: string) =>
    apiFetch<void>(`/api/jobs/${encodeURIComponent(id)}/cancel`, { method: 'POST' }),

  people: (signal?: AbortSignal) => apiFetch<PeopleResponse>('/api/members', { signal }),
  inviteMember: (name: string, email: string | null) =>
    apiFetch<InviteCreated>('/api/members', { method: 'POST', body: { name, email } }),
  renewInvite: (id: number) =>
    apiFetch<InviteCreated>(`/api/members/${encodeURIComponent(id)}/invite`, { method: 'POST' }),
  removeMember: (id: number) =>
    apiFetch<Member>(`/api/members/${encodeURIComponent(id)}/remove`, { method: 'POST' }),
}

/** URLs the browser loads directly (cookie-authenticated, versioned for immutable caching). */
export const urls = {
  thumbnail: (id: string, version: number) =>
    `/api/photos/${encodeURIComponent(id)}/thumbnail?v=${version}`,
  preview: (id: string, version: number) =>
    `/api/photos/${encodeURIComponent(id)}/preview?v=${version}`,
  downloadContent: (id: string) => `/api/downloads/${encodeURIComponent(id)}/content`,
  login: (returnUrl: string) => `/api/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`,
  googleLogin: (returnUrl: string) =>
    `/api/auth/login/google?returnUrl=${encodeURIComponent(returnUrl)}`,
  /** The link an admin sends: the token is in the #fragment, which browsers never send to any server. */
  invite: (token: string) => `${window.location.origin}/invite#${token}`,
}
