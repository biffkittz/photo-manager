import { ApiError, currentCsrfToken, notifyUnauthorized } from './http'
import type { ProblemDetails, UploadAccepted } from './types'

/**
 * Uploads one file with XMLHttpRequest, because fetch() cannot report upload progress. One file per request
 * lets the UI show per-file progress and retry individual failures.
 */
export function uploadFile(
  file: File,
  batchId: string,
  onProgress: (fraction: number) => void,
  signal?: AbortSignal,
): Promise<UploadAccepted> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', `/api/uploads?batchId=${encodeURIComponent(batchId)}`)
    xhr.responseType = 'json'
    xhr.setRequestHeader('Accept', 'application/json')
    const token = currentCsrfToken()
    if (token) {
      xhr.setRequestHeader('X-CSRF-TOKEN', token)
    }

    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) {
        onProgress(event.loaded / event.total)
      }
    }
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(xhr.response as UploadAccepted)
        return
      }

      if (xhr.status === 401) {
        // Expired session: send the user to sign in instead of failing every queued file one by one.
        notifyUnauthorized()
      }

      const problem = xhr.response as ProblemDetails | null
      reject(
        new ApiError(
          xhr.status,
          problem?.title ?? `Upload failed (HTTP ${xhr.status}).`,
          problem ?? undefined,
        ),
      )
    }
    xhr.onerror = () => reject(new ApiError(0, 'Network error while uploading.'))
    xhr.onabort = () => reject(new DOMException('Upload cancelled.', 'AbortError'))
    signal?.addEventListener('abort', () => xhr.abort(), { once: true })

    const form = new FormData()
    form.append('file', file, file.name)
    xhr.send(form)
  })
}
