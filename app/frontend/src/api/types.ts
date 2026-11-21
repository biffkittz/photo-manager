/**
 * DTOs mirroring the API contracts in app/backend/src/PhotoManager.Api/Contracts.
 * Enums are serialized as strings by the API.
 */

export type ProcessingState = 'NotStarted' | 'Pending' | 'Ready' | 'Failed' | 'Unsupported'
export type PhotoStatus = 'Queued' | 'Processing' | 'Ready' | 'Failed' | 'Duplicate' | 'Missing'
export type DescriptionSource = 'Gemini' | 'User'
export type DownloadStatus = 'Pending' | 'Ready' | 'Failed'
export type JobKind = 'ReindexArchive' | 'BackfillRenditions' | 'BackfillDescriptions'
export type JobStatus = 'Queued' | 'Running' | 'Completed' | 'Failed' | 'Cancelled'
export type Role = 'Admin' | 'Member'
export type GallerySort = 'uploaded' | 'taken'

export type SignInProvider = 'github' | 'google'

export interface UserInfo {
  /** GitHub id (positive) or invited member id (negative). */
  id: number
  /** GitHub login, or an invited member's name. */
  login: string
  name: string | null
  avatarUrl: string | null
  role: Role
  provider: SignInProvider
}

export interface SessionResponse {
  authenticated: boolean
  user: UserInfo | null
  /** Also issued before sign-in (opening an invite link is a POST). */
  csrfToken: string | null
  /** Whether the sign-in page offers Google (family members invited from the People page). */
  googleSignIn: boolean
}

export type MemberStatus = 'Invited' | 'Expired' | 'Active' | 'Removed'

/** Someone invited from the People page (signs in with Google). */
export interface Member {
  id: number
  name: string
  status: MemberStatus
  googleEmail: string | null
  inviteEmail: string | null
  invitedBy: string
  inviteExpiresAt: string | null
  joinedAt: string | null
  lastSignInAt: string | null
  removedAt: string | null
}

/** A GitHub account allowed by configuration (Terraform), shown read-only. */
export interface ConfiguredAccount {
  login: string
  role: Role
}

export interface PeopleResponse {
  members: Member[]
  gitHubAccounts: ConfiguredAccount[]
  googleSignIn: boolean
}

/** A new invite link; the token is returned only once. */
export interface InviteCreated {
  member: Member
  token: string
  expiresAt: string
}

export interface InvitePreview {
  name: string
  invitedBy: string
  expiresAt: string
  forSpecificAccount: boolean
}

export interface PhotoSummary {
  id: string
  fileName: string
  description: string | null
  uploadedAt: string
  takenAt: string | null
  width: number | null
  height: number | null
  renditionState: ProcessingState
  renditionVersion: number
}

export interface PhotoPage {
  items: PhotoSummary[]
  nextCursor: string | null
}

/** A year, or one month of it, that the gallery is narrowed to (the viewer's calendar). */
export interface GalleryPeriod {
  year: number
  /** 1 to 12; absent for the whole year. */
  month?: number
}

/** How many photos were taken (else uploaded) in one month of the viewer's calendar. */
export interface TimelineMonth {
  year: number
  month: number
  count: number
}

/** Photos taken on today's date in one earlier year. */
export interface OnThisDayYear {
  year: number
  yearsAgo: number
  photos: PhotoSummary[]
}

/** An album (shared by the whole family). */
export interface AlbumSummary {
  id: string
  name: string
  photoCount: number
  /** Capture (else upload) date of its oldest and newest photo; null when empty. */
  firstDate: string | null
  lastDate: string | null
  /** The chosen cover while it is in the album, else the first photo; null when empty. */
  cover: PhotoSummary | null
}

export interface PhotoDetails extends PhotoSummary {
  /** The name the photo arrived with; `fileName` follows its AI description once it has one. */
  originalFileName: string
  descriptionSource: DescriptionSource | null
  descriptionState: ProcessingState
  tags: string[]
  contentType: string
  sizeBytes: number | null
  uploadedBy: string | null
  status: PhotoStatus
  statusDetail: string | null
  encryptionFormat: 'LegacyV1' | 'V2' | null
  source: 'Archive' | 'Upload'
}

export interface UploadAccepted {
  photoId: string
  status: PhotoStatus
}

export interface UploadItemStatus {
  photoId: string
  fileName: string
  status: PhotoStatus
  statusDetail: string | null
  description: string | null
  descriptionState: ProcessingState
  renditionState: ProcessingState
  renditionVersion: number
  uploadedAt: string
}

export interface DownloadResponse {
  id: string
  status: DownloadStatus
  fileName: string
  sizeBytes: number | null
  error: string | null
  expiresAt: string
  /** Photos included in the download. */
  photoCount: number
  /** Requested photos left out because they were no longer available. */
  skippedCount: number
}

export interface JobResponse {
  id: string
  kind: JobKind
  status: JobStatus
  requestedBy: string
  createdAt: string
  startedAt: string | null
  completedAt: string | null
  processed: number
  succeeded: number
  failed: number
  summary: string | null
}

export interface LibraryOverview {
  totalPhotos: number
  missingRenditions: number
  missingDescriptions: number
  failedUploads: number
  missingFromArchive: number
}

/** RFC 9457 problem details returned by the API for errors. */
export interface ProblemDetails {
  /** Machine-readable kind of problem (for example the CSRF rejection, see `CSRF_PROBLEM_TYPE`). */
  type?: string
  title?: string
  status?: number
  detail?: string
  errors?: Record<string, string[]>
}
