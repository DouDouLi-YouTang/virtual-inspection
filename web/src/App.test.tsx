import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import App from './App'
import type { Issue } from './types'

const issue: Issue = {
  id: '12345678-abcd-4000-8000-123456789012',
  title: '1 号机柜温度异常',
  description: '机柜右侧温度明显偏高',
  priority: 'high',
  status: 'open',
  position: { x: 1.25, y: 0.8, z: 2.4 },
  createdAt: '2026-10-26T07:00:00Z',
  updatedAt: '2026-10-26T07:00:00Z',
}

function renderApp() {
  const client = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
  return render(
    <QueryClientProvider client={client}>
      <App />
    </QueryClientProvider>,
  )
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

describe('App', () => {
  it('renders issues loaded from the backend', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(jsonResponse([issue])),
    )
    renderApp()

    expect(
      await screen.findByRole('heading', { name: issue.title }),
    ).toBeInTheDocument()
    expect(screen.getByText('高优先级')).toBeInTheDocument()
    expect(screen.getByLabelText(/修改“1 号机柜温度异常”/)).toHaveValue('open')
    expect(screen.getByText('机柜右侧温度明显偏高')).toBeInTheDocument()
  })

  it('shows an empty state', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([])))
    renderApp()

    expect(await screen.findByText('还没有上报记录')).toBeInTheDocument()
  })

  it('shows a useful error instead of a blank page when backend is down', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockRejectedValue(new TypeError('fetch failed')),
    )
    renderApp()

    expect(await screen.findByRole('alert')).toHaveTextContent(
      '无法连接巡检后端',
    )
    expect(screen.getByRole('heading', { name: '问题管理台' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '重新连接' })).toBeInTheDocument()
  })

  it('patches status and refreshes the list', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse([issue]))
      .mockResolvedValueOnce(jsonResponse({ ...issue, status: 'in_progress' }))
      .mockResolvedValueOnce(
        jsonResponse([{ ...issue, status: 'in_progress' }]),
      )
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    renderApp()

    const select = await screen.findByLabelText(/修改“1 号机柜温度异常”/)
    await user.selectOptions(select, 'in_progress')

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        `/api/issues/${issue.id}`,
        expect.objectContaining({
          method: 'PATCH',
          body: JSON.stringify({ status: 'in_progress' }),
        }),
      )
    })
    await waitFor(() => {
      expect(screen.getByLabelText(/修改“1 号机柜温度异常”/)).toHaveValue(
        'in_progress',
      )
    })
  })

  it('keeps the page usable when status update fails', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse([issue]))
      .mockResolvedValueOnce(jsonResponse({ error: 'issue not found' }, 404))
      .mockResolvedValueOnce(jsonResponse([issue]))
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    renderApp()

    const select = await screen.findByLabelText(/修改“1 号机柜温度异常”/)
    await user.selectOptions(select, 'resolved')

    expect(await screen.findByText('issue not found')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: issue.title })).toBeInTheDocument()
  })
})

