---
name: unity-cli
description: 通过 UnityREPL 与 Unity Editor 进行进程间通信。支持持久化 stdin/stdout 流，用 C# 风格的调用语法直接在 Unity 主线程中运行逻辑，响应为纯文本。
---

# UnityREPL

UnityREPL 是一个持久化的 C# REPL，通过文件 IPC 直接在 Unity Editor 主线程上执行任意 C# 代码。原有的 repl JSON-RPC 封装已被移除，现在必须直接执行原生的 C# 语句。

## 启动

```bash
bun run Packages/com.lambda-labs.unity-repl/ts/src/repl.ts
```

建议使用 `RunPersistent=true` 保持终端存活，然后后续通过 `send_command_input` 发送 C# 代码并检查输出。

## 基本使用

直接输入 C# 表达式或语句，每行（按回车执行）一个：

```csharp
EditorApplication.isPlaying                              // 测试是否在运行，输出 True / False
EditorApplication.isPlaying = true                       // 进入 Play Mode
AssetDatabase.Refresh()                                  // 刷新资源目录
Selection.activeGameObject?.name ?? "(none)"             // 获取当前选中物体的名字
var go = new GameObject("Test"); go.name                 // 创建物体或执行多条语句
SceneManager.GetActiveScene().name                       // 获取当前激活的场景名称
GameObject.FindObjectsOfType<Camera>().Length            // 泛型查找
```

## 预加载的命名空间

评估环境已自动预加载以下命名空间，调用时不需要完整指定。如果需要更特殊的命名空间，可以直接发送 `using Foo.Bar;`：

```csharp
using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
```

## 响应

如果语句有返回值（如 `1+1` 或者是求值属性 `EditorApplication.isPlaying`），将原样转成纯文本输出。
如果抛出异常或编译报错，则返回 `ERROR` 提示并在日志中打印。
结尾一定会有 `---END---` 标识符用来代表本次运行完成。

## 替代原有工具的方法参考库

现在所有的工具能力都是使用原生 C#，遇到各种场景请使用下方范例，或根据 Unity API 自行编写表达式执行：

### 控制与场景
| 功能 | 原生 C# 等价命令 |
|------|---------|
| 获取当前场景名 | `SceneManager.GetActiveScene().name` |
| 进入 Play Mode | `EditorApplication.isPlaying = true` |
| 停止 Play Mode | `EditorApplication.isPlaying = false` |
| 刷新/编译 | `AssetDatabase.Refresh()` |
| 打开指定的场景 | `EditorSceneManager.OpenScene("Assets/Scenes/Lab.unity")` |

### GameObject 与 实例化
| 功能 | 原生 C# 等价命令 |
|------|---------|
| 查找单个对象 | `GameObject.Find("Player") != null ? "Found" : "Missing"` |
| 输出所有的 Camera | `string.Join(", ", GameObject.FindObjectsOfType<Camera>().Select(c => c.name))` |
| 实例化预制体 | `var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cube.prefab"); var instance = PrefabUtility.InstantiatePrefab(p) as GameObject; instance.name` |
| 添加组件 | `var go = GameObject.Find("Player"); if(go != null) go.AddComponent<BoxCollider>();` |

### 日志与调试
| 功能 | 原生 C# 等价命令 |
|------|---------|
| 查看控制台错误数 | `var m = typeof(UnityEditor.LogEntries).GetMethod("GetCount", System.Reflection.BindingFlags.Static\|System.Reflection.BindingFlags.Public); m.Invoke(null,null)` |
| 截图 | `ScreenCapture.CaptureScreenshot("/tmp/shot.png")` |

> **提示给 AI**：遇到你需要的逻辑，自己临时在 REPL 中构思 C# 代码调用即可，例如查询某些类的属性，反射特定系统的状态。由于直接跑在主线程，你拥有近乎无限的访问权限。
