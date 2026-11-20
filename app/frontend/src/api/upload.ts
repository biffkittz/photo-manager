import {
  ApiError,
  currentCsrfToken,
  isCsrfProblem,
  notifyUnauthorized,
  refreshCsrfToken,
} from './http'
import type { ProblemDetails, UploadAccepted } from './types'

interface UploadResult {
  status: number
  response: unknown
}

/**
 * Uploads one file with XMLHttpRequest, because fetch() cannot report upload progress. One file per request
 * lets the UI show per-file progress and retry individual failures. With `describe` false the photo is stored
 * without an AI description (it can be described later), so the upload does not wait for Gemini.
 */
export async function uploadFile(
  file: File,
  batchId: string,
  onProgress: (fraction: number) => void,
  signal?: AbortSignal,
  describe = true,
): Promise<UploadAccepted> {
  const url = `/api/uploads?batchId=${encodeURIComponent(batchId)}${describe ? '' : '&describe=false'}`
  let result = await send(file, url, onProgress, signal)
  if (result.status === 403 && isCsrfProblem(result.response)) {
    // Stale anti-CSRF token (the API never reads the file without a valid one): fetch a fresh token and send the
    // file once more.
    await refreshCsrfToken()
    onProgress(0)
    result = await send(file, url, onProgress, signal)
  }

  if (result.status >= 200 && result.status < 300) {
    return result.response as UploadAccepted
  }

  if (result.status === 401) {
    // Expired session: send the user to sign in instead of failing every queued file one by one.
    notifyUnauthorized()
  }

  const problem = result.response as ProblemDetails | null
  throw new ApiError(
    result.status,
    problem?.title ?? `Upload failed (HTTP ${result.status}).`,
    problem ?? undefined,
  )
}

function send(
  file: File,
  url: string,
  onProgress: (fraction: number) => void,
  signal?: AbortSignal,
): Promise<UploadResult> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) {
      reject(new DOMException('Upload cancelled.', 'AbortError'))
      return
    }

    const xhr = new XMLHttpRequest()
    xhr.open('POST', url)
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
    xhr.onload = () => resolve({ status: xhr.status, response: xhr.response as unknown })
    xhr.onerror = () => reject(new ApiError(0, 'Network error while uploading.'))
    xhr.onabort = () => reject(new DOMException('Upload cancelled.', 'AbortError'))
    signal?.addEventListener('abort', () => xhr.abort(), { once: true })

    const form = new FormData()
    form.append('file', file, file.name)
    xhr.send(form)
  })
}
