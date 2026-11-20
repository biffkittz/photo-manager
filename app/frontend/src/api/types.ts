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

export interface UserInfo {
  gitHubId: number
  login: string
  name: string | null
  avatarUrl: string | null
  role: Role
}

export interface SessionResponse {
  authenticated: boolean
  user: UserInfo | null
  csrfToken: string | null
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

export interface PhotoDetails extends PhotoSummary {
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
  title?: string
  status?: number
  detail?: string
  errors?: Record<string, string[]>
}
