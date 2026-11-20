import { apiFetch } from './http'
import type {
  DownloadResponse,
  GallerySort,
  JobKind,
  InviteCreated,
  InvitePreview,
  JobResponse,
  LibraryOverview,
  Member,
  PeopleResponse,
  PhotoDetails,
  PhotoPage,
  SessionResponse,
  UploadItemStatus,
} from './types'

/** Typed wrappers for every API endpoint the SPA uses. */
export const api = {
  session: (signal?: AbortSignal) => apiFetch<SessionResponse>('/api/auth/session', { signal }),
  logout: () => apiFetch<void>('/api/auth/logout', { method: 'POST' }),
  /** Opens an invite link: the token comes from the link's #fragment and is only ever sent in this body. */
  redeemInvite: (token: string) =>
    apiFetch<InvitePreview>('/api/auth/invites/redeem', { method: 'POST', body: { token } }),

  listPhotos: (
    params: { q?: string; cursor?: string | null; sort?: GallerySort; limit?: number },
    signal?: AbortSignal,
  ) => {
    const query = new URLSearchParams()
    if (params.q) query.set('q', params.q)
    if (params.cursor) query.set('cursor', params.cursor)
    if (params.sort) query.set('sort', params.sort)
    if (params.limit) query.set('limit', String(params.limit))
    return apiFetch<PhotoPage>(`/api/photos?${query.toString()}`, { signal })
  },
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

  createDownload: (photoIds: string[]) =>
    apiFetch<DownloadResponse>('/api/downloads', { method: 'POST', body: { photoIds } }),
  download: (id: string, signal?: AbortSignal) =>
    apiFetch<DownloadResponse>(`/api/downloads/${encodeURIComponent(id)}`, { signal }),

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
