import { useEffect, useMemo, useState } from 'react'
import {
  useMutation,
  useQuery,
  useQueryClient,
  type QueryKey,
} from '@tanstack/react-query'
import { deleteIssue, fetchIssues, updateIssueStatus } from './api'
import type { Issue, IssueStatus, Priority } from './types'

const issuesQueryKey: QueryKey = ['issues']

const priorityLabels: Record<Priority, string> = {
  low: '低',
  medium: '中',
  high: '高',
}

const statusLabels: Record<IssueStatus, string> = {
  open: '待处理',
  in_progress: '处理中',
  resolved: '已解决',
}

const statusOptions: IssueStatus[] = ['open', 'in_progress', 'resolved']

function formatTime(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return new Intl.DateTimeFormat('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date)
}

function IssueCard({
  issue,
  onStatusChange,
  onOpenDetails,
  onDelete,
  isUpdating,
  isDeleting,
}: {
  issue: Issue
  onStatusChange: (status: IssueStatus) => void
  onOpenDetails: () => void
  onDelete: () => void
  isUpdating: boolean
  isDeleting: boolean
}) {
  return (
    <article
      className={`issue-card priority-${issue.priority}`}
      aria-label={issue.title}
    >
      <div className="card-rail" aria-hidden="true" />
      <div className="card-heading">
        <div>
          <div className="card-meta">
            <span className={`priority-badge ${issue.priority}`}>
              {priorityLabels[issue.priority]}优先级
            </span>
            <span className="issue-id">#{issue.id.slice(0, 8)}</span>
          </div>
          <h2>{issue.title}</h2>
        </div>
        <span className={`status-badge ${issue.status}`}>
          {statusLabels[issue.status]}
        </span>
      </div>

      <p className={issue.description ? 'description' : 'description muted'}>
        {issue.description || '未填写问题描述'}
      </p>

      <div className="card-footer">
        <dl className="details">
          <div>
            <dt>现场坐标</dt>
            <dd>
              {issue.position.x.toFixed(2)}, {issue.position.y.toFixed(2)},{' '}
              {issue.position.z.toFixed(2)}
            </dd>
          </div>
          <div>
            <dt>上报时间</dt>
            <dd>{formatTime(issue.createdAt)}</dd>
          </div>
        </dl>

        <div className="card-actions">
          <label className="status-control">
            <span>处理状态</span>
            <select
              value={issue.status}
              disabled={isUpdating || isDeleting}
              onChange={(event) =>
                onStatusChange(event.target.value as IssueStatus)
              }
              aria-label={`修改“${issue.title}”的处理状态`}
            >
              {statusOptions.map((status) => (
                <option key={status} value={status}>
                  {statusLabels[status]}
                </option>
              ))}
            </select>
          </label>
          <button
            className="details-button"
            type="button"
            onClick={onOpenDetails}
          >
            查看详情
          </button>
          <button
            className="delete-button"
            type="button"
            onClick={onDelete}
            disabled={isDeleting}
            aria-label={`删除“${issue.title}”`}
          >
            {isDeleting ? '删除中…' : '删除'}
          </button>
        </div>
      </div>
    </article>
  )
}

function IssueDetail({
  issue,
  onClose,
  onDelete,
  isDeleting,
}: {
  issue: Issue
  onClose: () => void
  onDelete: () => void
  isDeleting: boolean
}) {
  useEffect(() => {
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose()
      }
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => {
      document.body.style.overflow = previousOverflow
      window.removeEventListener('keydown', handleKeyDown)
    }
  }, [onClose])

  return (
    <div className="detail-backdrop" role="presentation" onClick={onClose}>
      <aside
        className="detail-drawer"
        role="dialog"
        aria-modal="true"
        aria-labelledby="detail-title"
        onClick={(event) => event.stopPropagation()}
      >
        <div className="detail-heading">
          <div>
            <span className={`priority-badge ${issue.priority}`}>
              {priorityLabels[issue.priority]}优先级
            </span>
            <p className="detail-id">#{issue.id}</p>
          </div>
          <button
            className="detail-close"
            type="button"
            onClick={onClose}
            aria-label="关闭详情"
          >
            ×
          </button>
        </div>
        <h2 id="detail-title">{issue.title}</h2>
        <p className="detail-description">
          {issue.description || '未填写问题描述'}
        </p>
        <dl className="detail-facts">
          <div>
            <dt>当前状态</dt>
            <dd>{statusLabels[issue.status]}</dd>
          </div>
          <div>
            <dt>现场坐标</dt>
            <dd>
              X {issue.position.x.toFixed(2)} · Y{' '}
              {issue.position.y.toFixed(2)} · Z{' '}
              {issue.position.z.toFixed(2)}
            </dd>
          </div>
          <div>
            <dt>上报时间</dt>
            <dd>{formatTime(issue.createdAt)}</dd>
          </div>
          <div>
            <dt>最后更新</dt>
            <dd>{formatTime(issue.updatedAt)}</dd>
          </div>
        </dl>
        <div className="detail-footer">
          <button
            className="delete-button solid"
            type="button"
            onClick={onDelete}
            disabled={isDeleting}
          >
            {isDeleting ? '删除中…' : '删除此问题'}
          </button>
          <button
            className="details-button"
            type="button"
            onClick={onClose}
          >
            返回列表
          </button>
        </div>
      </aside>
    </div>
  )
}

export default function App() {
  const queryClient = useQueryClient()
  const [updateError, setUpdateError] = useState('')
  const [updatingId, setUpdatingId] = useState('')
  const [deletingId, setDeletingId] = useState('')
  const [keyword, setKeyword] = useState('')
  const [statusFilter, setStatusFilter] = useState<IssueStatus | 'all'>('all')
  const [priorityFilter, setPriorityFilter] = useState<Priority | 'all'>('all')
  const [selectedIssue, setSelectedIssue] = useState<Issue | null>(null)

  const issuesQuery = useQuery({
    queryKey: issuesQueryKey,
    queryFn: fetchIssues,
  })

  const updateMutation = useMutation({
    mutationFn: ({
      id,
      status,
    }: {
      id: string
      status: IssueStatus
    }) => updateIssueStatus(id, status),
    onMutate: async ({ id }) => {
      setUpdateError('')
      setUpdatingId(id)
      await queryClient.cancelQueries({ queryKey: issuesQueryKey })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: issuesQueryKey })
    },
    onError: (error) => {
      setUpdateError(
        error instanceof Error ? error.message : '状态更新失败，请稍后重试。',
      )
    },
    onSettled: () => {
      setUpdatingId('')
    },
  })

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteIssue(id),
    onMutate: (id) => {
      setUpdateError('')
      setDeletingId(id)
    },
    onSuccess: () => {
      setSelectedIssue(null)
      queryClient.invalidateQueries({ queryKey: issuesQueryKey })
    },
    onError: (error) => {
      setUpdateError(
        error instanceof Error ? error.message : '删除失败，请稍后重试。',
      )
    },
    onSettled: () => {
      setDeletingId('')
    },
  })

  const issueCount = issuesQuery.data?.length ?? 0
  const openCount =
    issuesQuery.data?.filter((issue) => issue.status !== 'resolved').length ?? 0
  const filteredIssues = useMemo(() => {
    const normalizedKeyword = keyword.trim().toLowerCase()
    return (issuesQuery.data ?? []).filter((issue) => {
      const matchesKeyword =
        normalizedKeyword.length === 0 ||
        issue.title.toLowerCase().includes(normalizedKeyword) ||
        issue.description.toLowerCase().includes(normalizedKeyword) ||
        issue.id.toLowerCase().includes(normalizedKeyword)
      const matchesStatus =
        statusFilter === 'all' || issue.status === statusFilter
      const matchesPriority =
        priorityFilter === 'all' || issue.priority === priorityFilter
      return matchesKeyword && matchesStatus && matchesPriority
    })
  }, [issuesQuery.data, keyword, priorityFilter, statusFilter])
  const hasActiveFilters =
    keyword.trim().length > 0 ||
    statusFilter !== 'all' ||
    priorityFilter !== 'all'

  function confirmDelete(issue: Issue) {
    if (window.confirm(`确定删除“${issue.title}”吗？删除后不可恢复。`)) {
      deleteMutation.mutate(issue.id)
    }
  }

  return (
    <div className="app-shell">
      <header className="masthead">
        <div className="brand">
          <span className="brand-mark" aria-hidden="true">
            <span />
            <span />
            <span />
          </span>
          <div>
            <p className="eyebrow">PARK INSPECTION / 巡检调度</p>
            <h1>问题管理台</h1>
          </div>
        </div>
        <p className="masthead-copy">
          集中查看 Unity 现场上报的位置与隐患，推进问题闭环。
        </p>
      </header>

      <main>
        <section className="summary" aria-label="问题概览">
          <div className="summary-main">
            <span className="summary-label">当前未闭环</span>
            <strong>{issuesQuery.isSuccess ? openCount : '—'}</strong>
            <span className="summary-unit">项</span>
          </div>
          <div className="summary-stat">
            <span>累计上报</span>
            <strong>{issuesQuery.isSuccess ? issueCount : '—'}</strong>
          </div>
          <div className="summary-stat">
            <span>数据连接</span>
            <strong className={issuesQuery.isError ? 'offline' : 'online'}>
              {issuesQuery.isError ? '离线' : '在线'}
            </strong>
          </div>
          <button
            className="refresh-button"
            type="button"
            onClick={() => void issuesQuery.refetch()}
            disabled={issuesQuery.isFetching}
          >
            {issuesQuery.isFetching ? '刷新中…' : '刷新问题'}
          </button>
        </section>

        {issuesQuery.isError && (
          <section className="error-panel" role="alert">
            <div>
              <strong>无法加载问题列表</strong>
              <p>
                {issuesQuery.error instanceof Error
                  ? issuesQuery.error.message
                  : '未知网络错误。'}
              </p>
            </div>
            <button type="button" onClick={() => void issuesQuery.refetch()}>
              重新连接
            </button>
          </section>
        )}

        {updateError && (
          <div className="inline-error" role="alert">
            <span>{updateError}</span>
            <button type="button" onClick={() => setUpdateError('')}>
              关闭
            </button>
          </div>
        )}

        {issuesQuery.isPending && (
          <section className="state-panel" aria-live="polite">
            <span className="loader" aria-hidden="true" />
            <p>正在连接巡检现场数据…</p>
          </section>
        )}

        {issuesQuery.isSuccess && issueCount === 0 && (
          <section className="state-panel empty">
            <span className="empty-symbol" aria-hidden="true">
              +
            </span>
            <h2>还没有上报记录</h2>
            <p>在 Unity 场景中点击设备并提交问题后，记录会出现在这里。</p>
          </section>
        )}

        {issuesQuery.isSuccess && issueCount > 0 && (
          <section className="issue-section">
            <div className="filter-panel" aria-label="筛选问题">
              <label className="search-control">
                <span>搜索</span>
                <input
                  type="search"
                  value={keyword}
                  onChange={(event) => setKeyword(event.target.value)}
                  placeholder="标题、描述或问题 ID"
                />
              </label>
              <label className="filter-control">
                <span>状态</span>
                <select
                  value={statusFilter}
                  onChange={(event) =>
                    setStatusFilter(
                      event.target.value as IssueStatus | 'all',
                    )
                  }
                >
                  <option value="all">全部状态</option>
                  {statusOptions.map((status) => (
                    <option key={status} value={status}>
                      {statusLabels[status]}
                    </option>
                  ))}
                </select>
              </label>
              <label className="filter-control">
                <span>优先级</span>
                <select
                  value={priorityFilter}
                  onChange={(event) =>
                    setPriorityFilter(
                      event.target.value as Priority | 'all',
                    )
                  }
                >
                  <option value="all">全部优先级</option>
                  <option value="high">高优先级</option>
                  <option value="medium">中优先级</option>
                  <option value="low">低优先级</option>
                </select>
              </label>
              <div className="filter-result">
                <span>显示</span>
                <strong>{filteredIssues.length}</strong>
                <span>/ {issueCount} 项</span>
                {hasActiveFilters && (
                  <button
                    type="button"
                    onClick={() => {
                      setKeyword('')
                      setStatusFilter('all')
                      setPriorityFilter('all')
                    }}
                  >
                    清除筛选
                  </button>
                )}
              </div>
            </div>
            <div className="section-heading">
              <h2>现场问题</h2>
              <span>
                {hasActiveFilters
                  ? `已筛选 ${filteredIssues.length} 项`
                  : '按上报时间倒序'}
              </span>
            </div>
            {filteredIssues.length === 0 ? (
              <section className="state-panel empty compact">
                <h2>没有符合筛选条件的问题</h2>
                <p>调整关键词、状态或优先级后重试。</p>
              </section>
            ) : (
              <div className="issue-grid">
                {filteredIssues.map((issue) => (
                  <IssueCard
                    key={issue.id}
                    issue={issue}
                    isUpdating={updatingId === issue.id}
                    isDeleting={deletingId === issue.id}
                    onOpenDetails={() => setSelectedIssue(issue)}
                    onDelete={() => confirmDelete(issue)}
                    onStatusChange={(status) =>
                      updateMutation.mutate({ id: issue.id, status })
                    }
                  />
                ))}
              </div>
            )}
          </section>
        )}
      </main>

      {selectedIssue && (
        <IssueDetail
          issue={
            issuesQuery.data?.find(
              (issue) => issue.id === selectedIssue.id,
            ) ?? selectedIssue
          }
          onClose={() => setSelectedIssue(null)}
          onDelete={() =>
            confirmDelete(
              issuesQuery.data?.find(
                (issue) => issue.id === selectedIssue.id,
              ) ?? selectedIssue,
            )
          }
          isDeleting={deletingId === selectedIssue.id}
        />
      )}

      <footer>
        <span>INSPECTION CONTROL</span>
        <span>Unity → Go → React</span>
      </footer>
    </div>
  )
}

