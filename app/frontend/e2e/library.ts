import type { Page, Request } from '@playwright/test'
import { admin, mockSession, pixelPng } from './fixtures'

export interface FakePhoto {
  id: string
  fileName: string
  description: string
  uploadedAt: string
  takenAt: string | null
  width: number
  height: number
  renditionState: 'Ready'
  renditionVersion: number
}

export interface FakeAlbum {
  id: string
  name: string
  coverPhotoId: string | null
  photoIds: string[]
}

export function photo(id: string, description: string, takenAt: string): FakePhoto {
  return {
    id,
    fileName: `${id}.jpg`,
    description,
    uploadedAt: '2026-11-01T12:00:00Z',
    takenAt,
    width: 800,
    height: 600,
    renditionState: 'Ready',
    renditionVersion: 1,
  }
}

/**
 * An in-memory stand-in for the photo and album API, enough to drive whole flows: albums can be created, changed,
 * downloaded and deleted, and the state shows what the app asked for. `requests` records every mutation.
 */
export async function mockLibrary(page: Page, photos: FakePhoto[], albums: FakeAlbum[] = []) {
  await mockSession(page, admin)
  const requests: { method: string; path: string; body: unknown }[] = []
  const byId = new Map(photos.map((p) => [p.id, p]))
  const sorted = (ids: string[]) =>
    ids
      .map((id) => byId.get(id)!)
      .sort((a, b) => (a.takenAt ?? a.uploadedAt).localeCompare(b.takenAt ?? b.uploadedAt))
  const summary = (album: FakeAlbum) => {
    const items = sorted(album.photoIds)
    return {
      id: album.id,
      name: album.name,
      photoCount: items.length,
      firstDate: items[0]?.takenAt ?? null,
      lastDate: items.at(-1)?.takenAt ?? null,
      cover: items.find((p) => p.id === album.coverPhotoId) ?? items[0] ?? null,
    }
  }
  const record = (request: Request) => {
    const path = new URL(request.url()).pathname
    const body: unknown = request.postData() ? request.postDataJSON() : null
    requests.push({ method: request.method(), path, body })
    return body as { name?: string; photoIds?: string[]; coverPhotoId?: string }
  }

  await page.route(/\/api\/photos\?/, (route) => {
    const q = new URL(route.request().url()).searchParams.get('q') ?? ''
    return route.fulfill({
      json: { items: photos.filter((p) => p.description.includes(q)), nextCursor: null },
    })
  })
  await page.route(/\/api\/photos\/p\d+\/(thumbnail|preview)/, (route) =>
    route.fulfill({ body: pixelPng, contentType: 'image/png' }),
  )
  await page.route(/\/api\/photos\/p\d+$/, (route) => {
    const found = byId.get(new URL(route.request().url()).pathname.split('/').at(-1)!)!
    return route.fulfill({
      json: {
        ...found,
        descriptionSource: 'Gemini',
        descriptionState: 'Ready',
        tags: [],
        contentType: 'image/jpeg',
        sizeBytes: 1234,
        uploadedBy: 'octo',
        status: 'Ready',
        statusDetail: null,
        encryptionFormat: 'V2',
        source: 'Upload',
      },
    })
  })
  await page.route(/\/api\/albums/, (route) => {
    const request = route.request()
    const [, , , id, part, action] = new URL(request.url()).pathname.split('/')
    const method = request.method()
    const body = method === 'GET' ? null : record(request)
    if (!id) {
      if (method === 'POST') {
        const album = {
          id: `a${albums.length + 1}`,
          name: body!.name!,
          coverPhotoId: null,
          photoIds: [...new Set(body!.photoIds)],
        }
        albums.unshift(album)
        return route.fulfill({ status: 201, json: summary(album) })
      }
      return route.fulfill({ json: albums.map(summary) })
    }

    const album = albums.find((a) => a.id === id)
    if (!album) return route.fulfill({ status: 404, json: { title: 'Not Found', status: 404 } })
    if (part === 'photos' && method === 'GET') {
      return route.fulfill({ json: { items: sorted(album.photoIds), nextCursor: null } })
    }
    if (part === 'photos' && action === 'remove') {
      album.photoIds = album.photoIds.filter((p) => !body!.photoIds!.includes(p))
    } else if (part === 'photos') {
      album.photoIds = [...new Set([...album.photoIds, ...body!.photoIds!])]
    } else if (method === 'PATCH') {
      album.name = body!.name ?? album.name
      album.coverPhotoId = body!.coverPhotoId ?? album.coverPhotoId
    } else if (method === 'DELETE') {
      albums.splice(albums.indexOf(album), 1)
      return route.fulfill({ status: 204 })
    }
    return route.fulfill({ json: summary(album) })
  })
  await page.route('**/api/downloads', (route) => {
    const body = record(route.request()) as { albumId?: string }
    const album = albums.find((a) => a.id === body.albumId)
    return route.fulfill({
      status: 202,
      json: {
        id: 'd1',
        status: 'Ready',
        fileName: album ? `${album.name}.zip` : 'photos.zip',
        sizeBytes: 3,
        error: null,
        expiresAt: '2030-01-01T00:00:00Z',
        photoCount: album?.photoIds.length ?? 1,
        skippedCount: 0,
      },
    })
  })
  return { albums, requests }
}
