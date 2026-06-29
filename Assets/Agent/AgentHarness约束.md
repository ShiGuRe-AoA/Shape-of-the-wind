# Unity VR Prototype Agent Harness

## 1. 项目基本信息

这是一个 Unity 3D 项目，使用 URP 渲染管线。

Unity 编辑器版本固定为：

```txt
Unity 2022.3.62f3c1
```

项目未来计划作为 VR 项目，后续会接入 VR 摄像头与 VR 手柄。目前阶段使用普通手柄进行输入模拟。

这是一个临时项目，目标是快速开发、快速验证玩法、快速迭代。项目更重视易理解、可运行、方便在 Inspector 中配置，不追求严格、复杂、长期维护型的项目架构。

## 2. Agent 总体工作原则

Agent 的首要目标是完成当前需求，并保证项目能在 Unity 2022.3.62f3c1 中正常编译和运行。

不要为了“架构优雅”引入过度设计。除非任务明确要求，否则不要主动引入复杂框架、事件总线、依赖注入、泛型管理器、过深继承层级、过度抽象接口、自动化资源流水线等内容。

优先使用简单、直观、容易在 Unity Inspector 中配置的 MonoBehaviour 组件。临时项目允许适度冗余，允许局部硬编码，但关键配置项应尽量暴露为 `[SerializeField]`，方便快速调试。

写代码时应优先让人能一眼看懂，而不是追求极限复用。类名、字段名、方法名要清楚表达用途。

禁止因为重构旧代码而破坏已实现功能。需要改动旧代码时，应尽量小步修改，并在回复中说明改动点和可能影响。

## 3. 文件与目录约定

推荐使用简洁目录结构，不需要过细分层。

```txt
Assets/
  _Project/
    Scenes/
    Scripts/
      Core/
      Input/
      Player/
      VR/
      Gameplay/
      UI/
      Tools/
    Prefabs/
    Materials/
    Shaders/
    Art/
    Audio/
    Settings/
```

目录说明：

```txt
Core      存放项目级基础脚本，例如简单 GameManager、SceneBootstrap 等
Input     存放输入读取、手柄模拟、未来 VR 输入适配相关脚本
Player    存放玩家控制、移动、交互、视角相关脚本
VR        存放未来 VR 摄像头、手柄、XR Rig 相关适配脚本
Gameplay  存放具体玩法逻辑
UI        存放 UI 面板、提示、HUD 相关逻辑
Tools     存放仅编辑器使用或调试辅助脚本
```

如当前项目已经存在目录结构，应优先沿用现有结构，不要强行迁移大量文件。

## 4. Unity 与 URP 约束

所有实现应兼容 Unity 2022.3.62f3c1。

项目使用 URP，不要使用仅适用于内置渲染管线的写法。涉及材质、Shader、后处理、相机效果时，应优先考虑 URP 的兼容性。

不要随意修改 Project Settings、URP Asset、Quality Settings、Input Settings 等全局设置。确实需要修改时，必须在回复中明确说明修改原因、修改位置和影响。

不要随意替换项目现有 URP Renderer、Render Feature、Lighting 设置。临时测试可以新建测试资源，但不要覆盖已有核心配置。

## 5. VR 未来兼容约束

当前阶段使用普通手柄模拟 VR 输入，但代码应避免把玩法逻辑直接绑定死在某一种输入设备上。

推荐做法：

```txt
玩法逻辑读取统一的输入封装
当前实现由 GamepadInputProvider 提供输入
未来可以增加 VRInputProvider 替换输入来源
```

不要在核心玩法代码中到处直接写：

```csharp
Input.GetKey(...)
Input.GetAxis(...)
Gamepad.current...
XRController...
```

除非只是临时 Debug 脚本。正式玩法代码应尽量通过一个输入读取组件或输入适配组件获取数据。

推荐的轻量结构：

```txt
PlayerInputReader
  负责向玩家控制、交互、UI 等系统提供统一输入数据

GamepadInputProvider
  当前阶段读取普通手柄输入

VRInputProvider
  未来接入 VR 手柄时再实现
```

这不是强制要求做复杂接口，只是要求输入来源尽量集中，避免后续接 VR 时全项目挖矿式查找输入代码。

## 6. 摄像头与玩家控制约束

由于项目未来会接入 VR 摄像头，当前阶段不要把摄像头逻辑写得过于死板。

当前可以使用普通 Camera 模拟玩家视角，也可以使用一个临时 Camera Rig。涉及玩家移动、视角朝向、交互射线时，应尽量把“相机来源”暴露为字段，而不是完全依赖 `Camera.main`。

推荐写法：

```csharp
[SerializeField] private Transform cameraRoot;
[SerializeField] private Camera playerCamera;
```

允许在 `Awake` 或 `Reset` 中自动查找默认相机，但必须允许 Inspector 手动覆盖。

玩家移动、交互、射线检测等逻辑，应尽量考虑未来 VR 头显和手柄射线的替换空间。

## 7. 输入模拟约束

当前使用普通手柄模拟 VR 手柄时，应在代码注释或字段名中明确这是临时模拟方案。

推荐映射思路：

```txt
左摇杆：玩家移动
右摇杆：视角旋转或转向模拟
手柄扳机：主要交互或抓取
肩键：次要交互
A / South Button：确认或使用
B / East Button：取消或返回
菜单键：暂停或调试菜单
```

实际按键映射应以当前任务需求为准。若用户没有指定，不要过度扩展输入表。

## 8. 代码风格

使用 C# 标准命名习惯：

```txt
类名：PascalCase
方法名：PascalCase
属性名：PascalCase
私有字段：camelCase 或 _camelCase，保持当前项目一致即可
常量：PascalCase 或 UPPER_CASE，保持当前项目一致即可
```

优先使用 `[SerializeField] private` 暴露 Inspector 配置，不建议为了 Inspector 暴露而滥用 public 字段。

每个主要类顶部应添加简短 XML Summary，说明这个类负责什么。不要写长篇注释，不要给每一行都写解释。

示例：

```csharp
/// <summary>
/// Reads temporary gamepad input and exposes it as player movement and interaction data.
/// This class is intended to be replaced or extended by a VR input provider later.
/// </summary>
public class GamepadInputProvider : MonoBehaviour
{
}
```

代码应尽量短小直接。一个脚本只负责一类主要事情，但不必为了单一职责原则拆得过细。

## 9. MonoBehaviour 生命周期约束

优先使用常见 Unity 生命周期：

```txt
Awake      获取组件引用、初始化本地数据
Start      依赖其他对象完成初始化后的逻辑
Update     输入读取、普通轮询
FixedUpdate 物理移动
OnEnable   注册事件
OnDisable  注销事件
```

注册事件必须在 `OnDisable` 中注销，避免对象禁用后继续收到事件。

不要在 `Update` 中频繁执行昂贵查找，例如：

```csharp
FindObjectOfType
GameObject.Find
Resources.Load
GetComponent 大量重复调用
```

必要引用应在 `Awake`、`Start` 或 Inspector 中准备。

## 10. Prefab 与 Inspector 配置约束

由于这是临时项目，允许大量配置通过 Inspector 完成。

Agent 在新增脚本时，应在回复中明确说明：

```txt
脚本应挂在哪个物体上
哪些字段需要拖引用
哪些字段可以保持默认
如何快速测试
```

如果新增 Prefab 依赖，应说明 Prefab 的建议层级。

示例：

```txt
PlayerRoot
  CameraRoot
    Main Camera
  HandSimulator_L
  HandSimulator_R
```

不要假设用户已经配置好了所有引用。对于关键引用，代码中应进行空值检查，并给出清晰 Warning 或 Error。

## 11. 物理与交互约束

涉及物理交互时，应明确使用 3D 物理还是 2D 物理。默认本项目是 U3D 项目，应优先使用 3D 物理组件：

```txt
Rigidbody
Collider
Physics.Raycast
Physics.OverlapSphere
```

不要误用 2D 物理 API，例如：

```txt
Rigidbody2D
Collider2D
Physics2D.Raycast
```

除非任务明确说明某个系统是 2D。

交互系统应优先保持简单。推荐使用射线检测或触发器检测，不要过早实现复杂交互框架。

## 12. UI 约束

UI 应优先使用 Unity UGUI，除非项目已有其他 UI 技术栈。

UI 脚本应暴露必要的 Text、Image、Button、Panel 引用给 Inspector。

临时 UI 可以直接由脚本控制显示隐藏，不要求 MVVM、数据绑定或复杂 UI 管理器。

涉及 VR UI 时，当前可先使用普通屏幕 UI 或 World Space Canvas 模拟。未来接 VR 时再根据需要替换为 VR 指针交互或世界空间面板。

## 13. 资源加载约束

临时项目优先使用直接引用资源的方式，不要主动引入 Addressables、AssetBundle、复杂资源管理器。

推荐：

```txt
[SerializeField] private AudioClip clip;
[SerializeField] private GameObject prefab;
[SerializeField] private Material material;
```

只有在用户明确要求动态加载、大量资源管理或运行时下载时，才考虑资源管理方案。

## 14. 日志与调试约束

允许使用 `Debug.Log`、`Debug.LogWarning`、`Debug.LogError` 进行调试。

日志内容应包含脚本或系统名前缀，方便筛选。

示例：

```csharp
Debug.LogWarning("[PlayerInteractor] No interactable object found.");
```

不要在每帧输出大量日志。需要每帧调试时，应加布尔开关。

示例：

```csharp
[SerializeField] private bool debugLogInput;
```

## 15. 性能约束

这是临时项目，不需要过早优化，但应避免明显低级性能问题。

禁止：

```txt
Update 中频繁 Find
Update 中大量 Instantiate / Destroy
无上限生成对象
每帧大量 Debug.Log
每帧重复创建大量 GC 对象
```

允许为了快速验证玩法写简单实现。性能优化应以“不明显卡顿、不阻塞开发”为准。

## 16. 版本控制与文件安全

禁止修改或删除：

```txt
.git/
.gitignore
ProjectSettings/VersionControlSettings.asset
```

不要主动重命名大量已有文件，不要批量迁移目录，不要删除未知用途资源。

如果必须修改已有核心文件，应尽量局部修改，并在回复中列出修改点。

## 17. 包管理约束

不要随意安装新的 Unity Package 或第三方插件。

如果任务确实需要新增包，应先说明：

```txt
为什么需要这个包
包名是什么
是否为 Unity 官方包
是否会影响现有项目
```

当前项目未来可能接入 VR，因此可以考虑 Unity XR Interaction Toolkit 或 OpenXR，但只有在用户明确要求接入 VR 阶段时才进行安装和配置。

## 18. Agent 回复格式要求

每次完成任务后，Agent 应输出以下内容：

```txt
1. 修改或新增了哪些文件
2. 每个文件的核心作用
3. Inspector 中需要如何配置
4. 如何在 Unity 中测试
5. 是否有已知限制或 TODO
```

如果任务存在不确定信息，Agent 应先根据当前上下文做最小可行实现，并在结尾提出需要用户确认的问题。

不要只说“已完成”。必须告诉用户如何验证。

## 19. 默认开发倾向

在没有额外说明时，Agent 应遵循以下默认倾向：

```txt
优先简单实现
优先 Inspector 可配置
优先当前能跑
优先减少文件数量
优先减少抽象
优先保留未来 VR 替换入口
```

不要为了未来可能存在的需求牺牲当前开发速度。

## 20. 当前阶段推荐 TODO

当前阶段建议优先完成：

```txt
1. 建立普通手柄输入模拟
2. 建立轻量玩家移动与视角控制
3. 建立简单交互射线或手柄指针模拟
4. 建立可替换的输入读取入口
5. 为未来 VR 摄像头与 VR 手柄预留最小接口
```

未来接入 VR 时再处理：

```txt
1. XR Rig
2. VR Camera
3. VR Controller
4. Hand Tracking 或 Controller Tracking
5. XR Interaction Toolkit
6. VR UI 指针交互
```

## 21. 不要做的事

除非用户明确要求，否则不要做以下事情：

```txt
不要重构成大型框架
不要引入复杂事件总线
不要引入依赖注入框架
不要引入 Addressables
不要安装新插件
不要主动改 Project Settings
不要重写整个输入系统
不要一次性生成大量脚本
不要把临时 Demo 写成大型商业项目结构
不要为了未来 VR 把当前手柄模拟做得过度复杂
```

## 22. 最终目标

本项目的目标不是做出完美架构，而是快速构建一个可运行、可演示、可继续迭代的 Unity URP VR 原型。

Agent 应始终服务于这个目标。
