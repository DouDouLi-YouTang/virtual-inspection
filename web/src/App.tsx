import { useState } from 'react'
import {
  useMutation,
  useQuery,
  useQueryClient,
  type QueryKey,
} from '@tanstack/react-query'
import { fetchIssues, updateIssueStatus } from './api'
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
  isUpdating,
}: {
  issue: Issue
  onStatusChange: (status: IssueStatus) => void
  isUpdating: boolean
}) {
  return (
    <article className={`issue-card priority-${issue.priority}`}>
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

        <label className="status-control">
          <span>处理状态</span>
          <select
            value={issue.status}
            disabled={isUpdating}
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
      </div>
    </article>
  )
}

export default function App() {
  const queryClient = useQueryClient()
  const [updateError, setUpdateError] = useState('')
  const [updatingId, setUpdatingId] = useState('')

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

  const issueCount = issuesQuery.data?.length ?? 0
  const openCount =
    issuesQuery.data?.filter((issue) => issue.status !== 'resolved').length ?? 0

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
            <div className="section-heading">
              <h2>现场问题</h2>
              <span>按上报时间倒序</span>
            </div>
            <div className="issue-grid">
              {issuesQuery.data.map((issue) => (
                <IssueCard
                  key={issue.id}
                  issue={issue}
                  isUpdating={updatingId === issue.id}
                  onStatusChange={(status) =>
                    updateMutation.mutate({ id: issue.id, status })
                  }
                />
              ))}
            </div>
          </section>
        )}
      </main>

      <footer>
        <span>INSPECTION CONTROL</span>
        <span>Unity → Go → React</span>
      </footer>
    </div>
  )
}

