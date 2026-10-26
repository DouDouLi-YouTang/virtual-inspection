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
    expect(
      screen.getByRole('article', { name: issue.title }),
    ).toHaveTextContent('高优先级')
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

  it('explains proxy failures that return a non-JSON response', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response('Bad Gateway', { status: 502 })),
    )
    renderApp()

    expect(await screen.findByRole('alert')).toHaveTextContent(
      '无法连接巡检后端（HTTP 502）',
    )
    expect(screen.getByRole('heading', { name: '问题管理台' })).toBeInTheDocument()
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

  it('filters by keyword, status and priority', async () => {
    const secondIssue: Issue = {
      ...issue,
      id: '87654321-abcd-4000-8000-123456789012',
      title: '冷却管线渗漏',
      description: '管线接头有水迹',
      priority: 'low',
      status: 'resolved',
    }
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(jsonResponse([issue, secondIssue])),
    )
    const user = userEvent.setup()
    renderApp()

    await screen.findByRole('heading', { name: issue.title })
    await user.type(screen.getByLabelText('搜索'), '管线')
    expect(screen.getByRole('heading', { name: secondIssue.title })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: issue.title })).not.toBeInTheDocument()

    await user.clear(screen.getByLabelText('搜索'))
    await user.selectOptions(screen.getByLabelText('状态'), 'resolved')
    expect(screen.getByRole('heading', { name: secondIssue.title })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: issue.title })).not.toBeInTheDocument()

    await user.selectOptions(screen.getByLabelText('优先级'), 'high')
    expect(screen.queryByRole('heading', { name: secondIssue.title })).not.toBeInTheDocument()
    expect(screen.getByText('没有符合筛选条件的问题')).toBeInTheDocument()
  })

  it('opens issue details', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(jsonResponse([issue])),
    )
    const user = userEvent.setup()
    renderApp()

    await screen.findByRole('heading', { name: issue.title })
    await user.click(screen.getByRole('button', { name: '查看详情' }))

    expect(screen.getByRole('dialog', { name: issue.title })).toBeInTheDocument()
    expect(screen.getByText(`#${issue.id}`)).toBeInTheDocument()
    expect(screen.getByText('最后更新')).toBeInTheDocument()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('deletes an issue after confirmation and refreshes the list', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse([issue]))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(jsonResponse([]))
    vi.stubGlobal('fetch', fetchMock)
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    const user = userEvent.setup()
    renderApp()

    await screen.findByRole('heading', { name: issue.title })
    await user.click(screen.getByRole('button', { name: `删除“${issue.title}”` }))

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        `/api/issues/${issue.id}`,
        expect.objectContaining({ method: 'DELETE' }),
      )
    })
    expect(await screen.findByText('还没有上报记录')).toBeInTheDocument()
  })
})

