# 虚拟巡检最小闭环

园区巡检人员可在 Unity 场景中点击设备或墙面，按实际射线命中点放置问题标记，填写标题、描述和优先级后提交到 Go 后端；管理人员在 React 页面查看问题并修改处理状态。

本仓库完成题目必做闭环，并实现 React 筛选/详情/删除、Unity 已保存标记恢复与点击查看等加分项；不包含真实 AR、手机 AR 和登录权限。

## 技术版本

| 部分 | 实际版本 / 方案 |
| --- | --- |
| Unity | 工程按 Unity 2022.3 LTS 编写；当前本机已由团结引擎 1.11.0（2022.3.62t17）打开并转换 |
| Go | Go 1.27.0，Gin 1.10.1，GORM 1.30.1，纯 Go SQLite 驱动 |
| React | React 19.3，TypeScript 5.8，Vite 7.3，TanStack Query 5.104 |
| 数据库 | SQLite，固定文件 `backend/data/inspection.db` |
| Node | v23.11.0，npm 11.6.1 |

## 目录

```text
backend/         Go API 与测试
web/             React 管理端
unity-client/    Unity 工程（保留 Assets、Packages、ProjectSettings）
scripts/         本机启动脚本
README.md
AI_LOG.md
```

## 启动

所有命令都在项目根目录执行。三个部分都依赖同一个 Go 服务。

### 1. 启动 Go 后端

```powershell
cd backend
go mod download
go run .
```

或：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-backend.ps1
```

服务地址固定为 `http://127.0.0.1:8080`，启动时自动创建 `backend/data/inspection.db`。

### 2. 启动 React 管理端

另开一个终端：

```powershell
cd web
npm install
npm run dev
```

或：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-web.ps1
```

浏览器打开 `http://127.0.0.1:5173`。Vite 将 `/api` 代理到 `http://127.0.0.1:8080`，浏览器不直接依赖额外域名。

### 3. 打开 Unity

用 Unity Hub 添加并打开 `unity-client`。工程使用 2022.3 LTS 的通用结构；Unity 2022.3 或团结引擎 1.11.0（2022.3.62t17）均可打开。打开 `Assets/Scenes/Main.unity`，点击 Play。

如果修改脚本后 Unity 以 **Safe Mode** 启动，先确认 Console 没有新的脚本错误，再点击 “Exit Safe Mode”；必要时关闭编辑器后重新打开工程，让 Unity 完整重编译一次。

操作：

- `WASD` 移动，`Shift` 加速。
- 按住鼠标右键，左右移动鼠标水平转向、上下移动鼠标俯仰视角，松开右键恢复鼠标。
- 鼠标左键点击机柜、管线、地面或墙面，在实际命中点放置标记。
- 表单打开后填写标题和描述，在始终可见的“低 / 中 / 高”按钮中选择优先级，点击“提交问题”。
- 点击“取消”会删除未保存标记，不调用后端。

场景和 UI 由 `InspectionApp.cs` 在运行时创建，因此不需要手工绑定场景对象。工程没有 AR Foundation、Cinemachine 或新输入系统依赖。

## 后端接口

统一后端：`http://127.0.0.1:8080`

### 健康检查

```http
GET /api/health
```

```json
{"status":"ok"}
```

### 获取问题列表

```http
GET /api/issues
```

返回 `Issue[]`，按 `createdAt` 倒序。

### 创建问题

```http
POST /api/issues
Content-Type: application/json
```

```json
{
  "title": "1 号机柜温度异常",
  "description": "机柜右侧温度明显偏高",
  "priority": "high",
  "position": {"x": 1.25, "y": 0.8, "z": 2.4}
}
```

成功返回 `201` 和完整 `Issue`。后端生成 UUID、`createdAt`、`updatedAt`，默认 `status` 为 `open`。

### 修改状态

```http
PATCH /api/issues/{id}
Content-Type: application/json

{"status":"in_progress"}
```

成功返回 `200` 和完整 `Issue`。允许的状态为 `open`、`in_progress`、`resolved`。

### 删除问题

```http
DELETE /api/issues/{id}
```

成功返回 `204 No Content`；问题不存在返回 `404`。删除后 SQLite 中的记录会被移除，不可恢复。

### 错误格式

```json
{"error":"title is required"}
```

至少覆盖：

- 空标题：`400`
- 非法优先级：`400`
- 非法状态：`400`
- 问题不存在：`404`

## 数据结构

三端字段名一致：

```json
{
  "id": "由后端生成",
  "title": "1 号机柜温度异常",
  "description": "机柜右侧温度明显偏高",
  "priority": "high",
  "status": "open",
  "position": {"x": 1.25, "y": 0.8, "z": 2.4},
  "createdAt": "由后端生成",
  "updatedAt": "由后端生成"
}
```

## 已完成

- Unity：基础巡检场景、可点击设备、WASD 移动、水平/垂直右键视角、射线命中点标记、准星与目标高亮、表单、取消清理、单待提交标记、提交成功/失败提示、UI 点击防穿透、提交中防重复、从后端恢复已保存标记、点击标记查看详情。
- Go：健康检查、创建/查询/修改状态/删除、UUID、枚举和标题校验、SQLite 持久化、CORS、单测。
- React：问题列表、关键词/状态/优先级筛选、详情抽屉、带确认的删除、三种状态修改、加载/空列表/网络错误/更新失败状态、响应式布局、组件测试。
- 文档：README、AI_LOG、启动脚本、Git 分阶段提交。

## 未完成与已知问题

- 未实现真实 AR、手机真机、AR Foundation 或 XR Simulation。
- 未实现登录和权限。
- Unity 生产代码和 EditMode 测试已在团结引擎 1.11.0 中实际编译通过，10 个 EditMode 测试全部通过；Play Mode 的手动操作仍由录制人按下方清单执行。
- Unity 使用操作系统 CJK 字体（优先 Microsoft YaHei），在缺少中文字体的机器上会回退到内置字体，中文可能显示为方框。
- SQLite 为单进程单连接配置，适合本演示，不是多实例并发生产方案。
- 后端只监听本机，CORS 只允许本地 Vite 端口，不做公网部署。
- Unity 恢复标记依赖后端当时返回的固定 Unity 世界坐标；如果虚拟场景坐标体系改变，历史标记也需要同步迁移。

## 验证记录

已执行并通过：

```powershell
cd backend
go test ./...
go build -o inspection-backend.exe .
```

```powershell
cd web
npm run build
npm test
npm run lint
```

- Go 测试覆盖健康检查、创建、列表、三种状态、空标题、非法枚举、未知 ID、删除成功/重复删除、关闭并重开数据库后的持久化。
- React 的 9 个测试覆盖正常加载、空列表、后端不可达、非 JSON 代理错误、状态更新成功和失败、关键词/状态/优先级筛选、详情打开/关闭、删除确认和刷新。
- 已启动真实 Go 进程与 Vite 页面，通过浏览器验证读取 SQLite 数据、状态写回、后端停止后的错误提示和重试入口。
- Unity 生产代码已通过 Unity 2022.3 / 团结引擎 1.11.0 编译；`InspectionApp.Tests.dll` 的 10 个 EditMode 测试全部通过，包含水平/垂直视角旋转和低/中/高优先级回归测试。

## Unity 程序集说明

- `Assets/Scripts/InspectionApp.Runtime.asmdef` 将运行时代码放入独立程序集。
- `Assets/Tests/EditMode/InspectionApp.Tests.asmdef` 只引用 `InspectionApp.Runtime`，不要改回 `Assembly-CSharp`。
- 如果修改脚本后 Unity 进入 Safe Mode，保存并关闭编辑器后重新打开工程，或点击退出 Safe Mode 让 Unity 重新编译。

## 视频录制清单

视频由项目负责人录制，建议两段各 3～5 分钟。

### 第一段：Unity 现场

1. 打开 Unity 2022.3，进入 `Main.unity`，点击 Play。
2. 用 WASD 移动，按住右键同时验证左右水平转向和上下俯仰，观察两个机柜和管线。
3. 点击 HUD 或输入框，证明不会误放场景标记。
4. 点击机柜或墙面，展示球体标记和坐标表单。
5. 填写标题、描述和优先级，提交，展示成功提示。
6. 再放一个标记后取消，展示未保存标记消失。
7. 展示准星在可点击目标上变橙色；点击已保存标记查看详情。
8. 可选：停止 Go 后端再提交一次，展示错误提示，确认表单和标记保留。

### 第二段：React 与持久化

1. 打开 `http://127.0.0.1:5173`，刷新并找到第一段提交的问题。
2. 使用关键词、状态和优先级筛选，展示筛选结果数量。
3. 打开详情抽屉，展示完整描述、坐标、上报/更新时间，并用 Esc 或关闭按钮退出。
4. 依次把状态改为 `in_progress`、`resolved`、`open`，每次展示页面刷新后的状态。
5. 删除一条测试记录，展示确认提示和删除后的列表。
6. 停止 Go 后端，刷新页面，展示错误面板和“重新连接”，确认没有白屏。
7. 重启 Go 后端，点击“重新连接”，展示数据仍在。

## AR 基础问题

### 1. 真实 AR 平面检测与点击放置需要哪些 AR Foundation 能力？

通常使用 `AR Session`、`XR Origin`、`AR Plane Manager` 检测平面，使用 `AR Raycast Manager` 将屏幕点击射线命中 `ARPlane` 或点云；在命中点创建 `ARAnchor`，再挂载虚拟标记。还需要 `AR Camera Manager` 处理相机画面，以及会话和跟踪状态管理。

### 2. 为什么只保存 Unity 的 x/y/z 通常不能保证下次启动对应同一真实位置？

Unity 坐标属于本次 AR 会话的 tracking space。会话重启、重新定位、设备漂移或跟踪丢失都可能改变世界原点；没有持久化 AR Anchor、会话地图或云锚点时，同一组数字不能稳定映射到现实中的同一点。

### 3. AR 初始化失败、设备不支持或 Tracking 异常时如何提示？

应明确显示当前状态，暂停放置操作，提供重试和“调整光照、移动设备、重新扫描”的建议；持续异常时退回普通 3D 预览或手动录入，不应静默继续放置。

## 开源许可证

本项目使用 [MIT License](LICENSE)。

