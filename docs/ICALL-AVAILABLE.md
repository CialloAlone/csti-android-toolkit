# 引擎侧 / 游戏侧 可用 ICall 权威清单（离线可查，替代「猜名字」）

> 生成自：`temp\audiohunt\out\libunity_thunks.txt`（libunity.so 的 853 条注册 thunk）
>        + `temp\il2cpp_icall_names.txt`（libil2cpp.so 里被引用的 wrapper 名字串）
> 总条目：**1154**（去重后跨两侧）

## ★★ 两条通用判据（全组通用）

> ### 判据 ①：**引擎 ICall 可用 ⇔ 名字在 libunity 的 853 条注册表里**
> ### 判据 ②：**游戏侧 ICall 可用 ⇔ 名字在 `libil2cpp.so` 的 wrapper 串里**
>
> **不成立的推论（都踩过）**：`libunity.so` 里有实现 ✗ / interop 元数据里有类 ✗ /
> 「在 `icall_inventory.txt` 里且不在 `icall_missing_real.txt` 里」✗ / 换名字变体（`_Injected`/`Impl`）✗。

> ### 判据 ③：**运行时地址 ↔ 文件偏移换算（不需要设备）**
> ```
> libunity_base = ScriptingMethodInvoke      # MelonLoader 日志里的 [DEBUG] ScriptingMethodInvoke found: 0x…
> file_offset   = runtime_addr − libunity_base
> ```
> 已用两次独立运行交叉验证：`ImageConversion::LoadImage` → 0x51C0A0、`Sprite::CreateSprite_Injected` → 0x3A43D8，
> 两次都精确一致。**任何运行时 ICall 地址都能离线翻译回 libunity 文件偏移。**

## ★ 怎么用这份清单（**重要，别误读**）

1. **在下面查到 = 可用**（引擎侧看 §分类表里的 `libunity impl` 列非空；游戏侧看是否列出）。
2. **查不到 = 不可用**，不要再试同名变体（`_Injected`/`Impl`/`Internal_` …）——无一例外。
   已确认查不到的例子：`AudioClip::Construct_Internal` / `SetData` / `CreateUserSound`、
   `Resources::FindObjectsOfTypeAll`、`AudioSource::PlayOneShotHelper`、
   `GUIClip::GetMatrix_Injected`、`SceneManagement.Scene::GetBuildIndexInternal`、
   `AudioSampleProvider::*`、`AssetBundle::*`、`Microphone::*`。
3. 需要重新生成本清单：`python temp\audiohunt\scan_thunks.py`（扫 libunity）+ `python temp\audiohunt\gen_icall_doc.py`。

---

## 我们当前实际依赖的 ICall（状态一览）

| ICall | 侧 | 状态 / 说明 |
|---|---|---|
| `UnityEngine.AudioClip::get_length()` | 引擎+游戏 | ✔ 读 clip 时长（AudioClip 里**唯一**可用的） |
| `UnityEngine.AudioSource::Play(System.Double)` | 引擎+游戏 | ✔ 播放音效 |
| `UnityEngine.AudioSource::PlayHelper(UnityEngine.AudioSource,System.UInt64)` | 引擎+游戏 | ✔ 播放音效 |
| `UnityEngine.AudioSource::SetPitch(UnityEngine.AudioSource,System.Single)` | 引擎+游戏 | ✔ 音高 |
| `UnityEngine.AudioSource::Stop(System.Boolean)` | 引擎+游戏 | ✔ 停止 |
| `UnityEngine.AudioSource::get_clip()` | 引擎+游戏 | ✔ 读 clip |
| `UnityEngine.AudioSource::get_isPlaying()` | 引擎+游戏 | ✔ 播放状态 |
| `UnityEngine.AudioSource::set_clip(UnityEngine.AudioClip)` | 引擎+游戏 | ✔ 绑定 clip |
| `UnityEngine.AudioSource::set_volume(System.Single)` | 引擎+游戏 | ✔ 音量 |
| `UnityEngine.GameObject::GetComponent(System.Type)` | 引擎+游戏 | ✔ 取组件 |
| `UnityEngine.GameObject::Internal_AddComponentWithType(System.Type)` | 引擎+游戏 | ✔ 加组件（AudioSource 等） |
| `UnityEngine.GameObject::Internal_CreateGameObject(UnityEngine.GameObject,System.String)` | 引擎+游戏 | ✔ 建 GameObject |
| `UnityEngine.ImageConversion::EncodeToPNG(UnityEngine.Texture2D)` | 引擎+游戏 | ✔ MiniLoader |
| `UnityEngine.ImageConversion::LoadImage(UnityEngine.Texture2D,System.Byte[],System.Boolean)` | 引擎+游戏 | ✔ MiniLoader 把 PNG 上传到纹理 |
| `UnityEngine.Mesh::Internal_Create(UnityEngine.Mesh)` | 引擎+游戏 | ✔ 网格 |
| `UnityEngine.Object::Destroy(UnityEngine.Object,System.Single)` | 引擎+游戏 | ✔ 销毁 |
| `UnityEngine.Object::DontDestroyOnLoad(UnityEngine.Object)` | 引擎+游戏 | ✔ 常驻对象 |
| `UnityEngine.Object::FindObjectsOfType(System.Type)` | 引擎+游戏 | ✔ 枚举活动场景对象（**只扫场景**） |
| `UnityEngine.Object::GetName(UnityEngine.Object)` | 引擎+游戏 | ✔ 取对象名 |
| `UnityEngine.Object::Internal_CloneSingle(UnityEngine.Object)` | 引擎+游戏 | ✔ Diag 克隆式创建 ScriptableObject |
| `UnityEngine.Resources::Load(System.String,System.Type)` | 引擎+游戏 | ✔ 探测用 |
| `UnityEngine.ScriptableObject::CreateScriptableObjectInstanceFromType(System.Type,System.Boolean)` | 引擎+游戏 | ✔ 创建 SO |
| `UnityEngine.Shader::Find(System.String)` | 引擎+游戏 | ✔ 着色器 |
| `UnityEngine.Sprite::CreateSprite_Injected(UnityEngine.Texture2D,UnityEngine.Rect&,UnityEngine.Vector2&,System.Single,System.UInt32,UnityEngine.SpriteMeshType,UnityEngine.Vector4&,System.Boolean)` | 引擎+游戏 | ✔ MiniLoader 建精灵（ImgBLK） |
| `UnityEngine.Sprite::get_bounds_Injected(UnityEngine.Bounds&)` | 引擎+游戏 | ✔ MiniLoader |
| `UnityEngine.Sprite::get_rect_Injected(UnityEngine.Rect&)` | 引擎+游戏 | ✔ MiniLoader |
| `UnityEngine.Sprite::get_texture()` | 引擎+游戏 | ✔ MiniLoader 回读精灵纹理 |
| `UnityEngine.Texture2D::ApplyImpl(System.Boolean,System.Boolean)` | 引擎+游戏 | ✔ 探测用 |
| `UnityEngine.Texture2D::Internal_CreateImpl(UnityEngine.Texture2D,System.Int32,System.Int32,System.Int32,UnityEngine.Experimental.Rendering.GraphicsFormat,UnityEngine.Experimental.Rendering.TextureCreationFlags,System.IntPtr)` | 引擎+游戏 | ✔ RawTexture.CreateTexture2D |
| `UnityEngine.Texture2D::SetPixelsImpl(System.Int32,System.Int32,System.Int32,System.Int32,UnityEngine.Color[],System.Int32,System.Int32)` | 引擎+游戏 | ✔ 探测用（实际改走 LoadImage） |

---

## UnityEngine（354）

| ICall | 侧 | libunity impl |
|---|---|---|
| `UnityEngine.AndroidJNI::AllocObject(System.IntPtr)` | 引擎+游戏 | impl=0x174E74 |
| `UnityEngine.AndroidJNI::AttachCurrentThread()` | 引擎+游戏 | impl=0x174B9C |
| `UnityEngine.AndroidJNI::CallBooleanMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x175868 |
| `UnityEngine.AndroidJNI::CallCharMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x175A48 |
| `UnityEngine.AndroidJNI::CallDoubleMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x175B90 |
| `UnityEngine.AndroidJNI::CallFloatMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x175AE8 |
| `UnityEngine.AndroidJNI::CallIntMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x1757C8 |
| `UnityEngine.AndroidJNI::CallLongMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x175C38 |
| `UnityEngine.AndroidJNI::CallObjectMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x175728 |
| `UnityEngine.AndroidJNI::CallSByteMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x1759A8 |
| `UnityEngine.AndroidJNI::CallShortMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x175908 |
| `UnityEngine.AndroidJNI::CallStaticBooleanMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x176050 |
| `UnityEngine.AndroidJNI::CallStaticCharMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x176230 |
| `UnityEngine.AndroidJNI::CallStaticDoubleMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x176378 |
| `UnityEngine.AndroidJNI::CallStaticFloatMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x1762D0 |
| `UnityEngine.AndroidJNI::CallStaticIntMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x175FB0 |
| `UnityEngine.AndroidJNI::CallStaticLongMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x176420 |
| `UnityEngine.AndroidJNI::CallStaticObjectMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x175F10 |
| `UnityEngine.AndroidJNI::CallStaticSByteMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x176190 |
| `UnityEngine.AndroidJNI::CallStaticShortMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x1760F0 |
| `UnityEngine.AndroidJNI::CallStaticStringMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x175E70 |
| `UnityEngine.AndroidJNI::CallStaticVoidMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x1764C0 |
| `UnityEngine.AndroidJNI::CallStringMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x175688 |
| `UnityEngine.AndroidJNI::CallVoidMethod(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x175CD8 |
| `UnityEngine.AndroidJNI::DeleteGlobalRef(System.IntPtr)` | 引擎+游戏 | impl=0x174E48 |
| `UnityEngine.AndroidJNI::DeleteLocalRef(System.IntPtr)` | 引擎+游戏 | impl=0x174E58 |
| `UnityEngine.AndroidJNI::DeleteWeakGlobalRef(System.IntPtr)` | 引擎+游戏 | impl=0x174E50 |
| `UnityEngine.AndroidJNI::DetachCurrentThread()` | 引擎+游戏 | impl=0x174BC0 |
| `UnityEngine.AndroidJNI::EnsureLocalCapacity(System.Int32)` | 引擎+游戏 | impl=0x174E70 |
| `UnityEngine.AndroidJNI::ExceptionClear()` | 引擎+游戏 | impl=0x174D8C |
| `UnityEngine.AndroidJNI::ExceptionDescribe()` | 引擎+游戏 | impl=0x174D88 |
| `UnityEngine.AndroidJNI::ExceptionOccurred()` | 引擎+游戏 | impl=0x174D84 |
| `UnityEngine.AndroidJNI::FatalError(System.String)` | 引擎+游戏 | impl=0x174D90 |
| `UnityEngine.AndroidJNI::FindClass(System.String)` | 引擎+游戏 | impl=0x174BDC |
| `UnityEngine.AndroidJNI::FromBooleanArray(System.IntPtr)` | 引擎+游戏 | impl=0x176BD8 |
| `UnityEngine.AndroidJNI::FromByteArray(System.IntPtr)` | 引擎+游戏 | impl=0x176C24 |
| `UnityEngine.AndroidJNI::FromCharArray(System.IntPtr)` | 引擎+游戏 | impl=0x176CBC |
| `UnityEngine.AndroidJNI::FromDoubleArray(System.IntPtr)` | 引擎+游戏 | impl=0x176E38 |
| `UnityEngine.AndroidJNI::FromFloatArray(System.IntPtr)` | 引擎+游戏 | impl=0x176DEC |
| `UnityEngine.AndroidJNI::FromIntArray(System.IntPtr)` | 引擎+游戏 | impl=0x176D54 |
| `UnityEngine.AndroidJNI::FromLongArray(System.IntPtr)` | 引擎+游戏 | impl=0x176DA0 |
| `UnityEngine.AndroidJNI::FromObjectArray(System.IntPtr)` | 引擎+游戏 | impl=0x176E84 |
| `UnityEngine.AndroidJNI::FromReflectedField(System.IntPtr)` | 引擎+游戏 | impl=0x174C94 |
| `UnityEngine.AndroidJNI::FromReflectedMethod(System.IntPtr)` | 引擎+游戏 | impl=0x174C90 |
| `UnityEngine.AndroidJNI::FromSByteArray(System.IntPtr)` | 引擎+游戏 | impl=0x176C70 |
| `UnityEngine.AndroidJNI::FromShortArray(System.IntPtr)` | 引擎+游戏 | impl=0x176D08 |
| `UnityEngine.AndroidJNI::GetArrayLength(System.IntPtr)` | 引擎+游戏 | impl=0x176ED0 |
| `UnityEngine.AndroidJNI::GetBooleanArrayElement(System.IntPtr,System.Int32)` | 引擎+游戏 | impl=0x176EF8 |
| `UnityEngine.AndroidJNI::GetBooleanField(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x175D78 |
| `UnityEngine.AndroidJNI::GetCharArrayElement(System.IntPtr,System.Int32)` | 引擎+游戏 | impl=0x176F10 |
| `UnityEngine.AndroidJNI::GetCharField(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x175D90 |
| `UnityEngine.AndroidJNI::GetDoubleArrayElement(System.IntPtr,System.Int32)` | 引擎+游戏 | impl=0x176F24 |
| `UnityEngine.AndroidJNI::GetDoubleField(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x175DA4 |
| `UnityEngine.AndroidJNI::GetFieldID(System.IntPtr,System.String,System.String)` | 引擎+游戏 | impl=0x17509C |
| `UnityEngine.AndroidJNI::GetFloatArrayElement(System.IntPtr,System.Int32)` | 引擎+游戏 | impl=0x176F20 |
| `UnityEngine.AndroidJNI::GetFloatField(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x175DA0 |
| `UnityEngine.AndroidJNI::GetIntArrayElement(System.IntPtr,System.Int32)` | 引擎+游戏 | impl=0x176F18 |
| `UnityEngine.AndroidJNI::GetIntField(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x175D98 |
| `UnityEngine.AndroidJNI::GetLongArrayElement(System.IntPtr,System.Int32)` | 引擎+游戏 | impl=0x176F1C |
| `UnityEngine.AndroidJNI::GetLongField(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x175D9C |
| `UnityEngine.AndroidJNI::GetMethodID(System.IntPtr,System.String,System.String)` | 引擎+游戏 | impl=0x174F68 |
| `UnityEngine.AndroidJNI::GetObjectArrayElement(System.IntPtr,System.Int32)` | 引擎+游戏 | impl=0x176F28 |
| `UnityEngine.AndroidJNI::GetObjectClass(System.IntPtr)` | 引擎+游戏 | impl=0x174F50 |
| `UnityEngine.AndroidJNI::GetObjectField(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x175D74 |
| `UnityEngine.AndroidJNI::GetSByteArrayElement(System.IntPtr,System.Int32)` | 引擎+游戏 | impl=0x176F0C |
| `UnityEngine.AndroidJNI::GetSByteField(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x175D8C |
| `UnityEngine.AndroidJNI::GetShortArrayElement(System.IntPtr,System.Int32)` | 引擎+游戏 | impl=0x176F14 |
| `UnityEngine.AndroidJNI::GetShortField(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x175D94 |
| `UnityEngine.AndroidJNI::GetStaticBooleanField(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x176560 |
| `UnityEngine.AndroidJNI::GetStaticCharField(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x176578 |
| `UnityEngine.AndroidJNI::GetStaticDoubleField(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x17658C |
| `UnityEngine.AndroidJNI::GetStaticFieldID(System.IntPtr,System.String,System.String)` | 引擎+游戏 | impl=0x175304 |
| `UnityEngine.AndroidJNI::GetStaticFloatField(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x176588 |
| `UnityEngine.AndroidJNI::GetStaticIntField(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x176580 |
| `UnityEngine.AndroidJNI::GetStaticLongField(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x176584 |
| `UnityEngine.AndroidJNI::GetStaticMethodID(System.IntPtr,System.String,System.String)` | 引擎+游戏 | impl=0x1751D0 |
| `UnityEngine.AndroidJNI::GetStaticObjectField(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x17655C |
| `UnityEngine.AndroidJNI::GetStaticSByteField(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x176574 |
| `UnityEngine.AndroidJNI::GetStaticShortField(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x17657C |
| `UnityEngine.AndroidJNI::GetStaticStringField(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x176558 |
| `UnityEngine.AndroidJNI::GetStringChars(System.IntPtr)` | 引擎+游戏 | impl=0x1755FC |
| `UnityEngine.AndroidJNI::GetStringField(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x175D70 |
| `UnityEngine.AndroidJNI::GetStringLength(System.IntPtr)` | 引擎+游戏 | impl=0x175600 |
| `UnityEngine.AndroidJNI::GetStringUTFChars(System.IntPtr)` | 引擎+游戏 | impl=0x175608 |
| `UnityEngine.AndroidJNI::GetStringUTFLength(System.IntPtr)` | 引擎+游戏 | impl=0x175604 |
| `UnityEngine.AndroidJNI::GetSuperclass(System.IntPtr)` | 引擎+游戏 | impl=0x174CB0 |
| `UnityEngine.AndroidJNI::GetVersion()` | 引擎+游戏 | impl=0x174BD8 |
| `UnityEngine.AndroidJNI::IsAssignableFrom(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x174CB4 |
| `UnityEngine.AndroidJNI::IsInstanceOf(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x174F54 |
| `UnityEngine.AndroidJNI::IsSameObject(System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x174E5C |
| `UnityEngine.AndroidJNI::NewBooleanArray(System.Int32)` | 引擎+游戏 | impl=0x176ED4 |
| `UnityEngine.AndroidJNI::NewCharArray(System.Int32)` | 引擎+游戏 | impl=0x176EDC |
| `UnityEngine.AndroidJNI::NewDoubleArray(System.Int32)` | 引擎+游戏 | impl=0x176EF0 |
| `UnityEngine.AndroidJNI::NewFloatArray(System.Int32)` | 引擎+游戏 | impl=0x176EEC |
| `UnityEngine.AndroidJNI::NewGlobalRef(System.IntPtr)` | 引擎+游戏 | impl=0x174E44 |
| `UnityEngine.AndroidJNI::NewIntArray(System.Int32)` | 引擎+游戏 | impl=0x176EE4 |
| `UnityEngine.AndroidJNI::NewLocalRef(System.IntPtr)` | 引擎+游戏 | impl=0x174E54 |
| `UnityEngine.AndroidJNI::NewLongArray(System.Int32)` | 引擎+游戏 | impl=0x176EE8 |
| `UnityEngine.AndroidJNI::NewObject(System.IntPtr,System.IntPtr,UnityEngine.jvalue[])` | 引擎+游戏 | impl=0x174E78 |
| `UnityEngine.AndroidJNI::NewObjectArray(System.Int32,System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x176EF4 |
| `UnityEngine.AndroidJNI::NewSByteArray(System.Int32)` | 引擎+游戏 | impl=0x176ED8 |
| `UnityEngine.AndroidJNI::NewShortArray(System.Int32)` | 引擎+游戏 | impl=0x176EE0 |
| `UnityEngine.AndroidJNI::NewString(System.Char[])` | 引擎+游戏 | impl=0x1754BC |
| `UnityEngine.AndroidJNI::NewStringFromStr(System.String)` | 引擎+游戏 | impl=0x175438 |
| `UnityEngine.AndroidJNI::NewStringUTF(System.String)` | 引擎+游戏 | impl=0x175548 |
| `UnityEngine.AndroidJNI::NewWeakGlobalRef(System.IntPtr)` | 引擎+游戏 | impl=0x174E4C |
| `UnityEngine.AndroidJNI::PopLocalFrame(System.IntPtr)` | 引擎+游戏 | impl=0x174E40 |
| `UnityEngine.AndroidJNI::PushLocalFrame(System.Int32)` | 引擎+游戏 | impl=0x174E3C |
| `UnityEngine.AndroidJNI::SetBooleanArrayElement(System.IntPtr,System.Int32,System.Boolean)` | 引擎+游戏 | impl=0x176F2C |
| `UnityEngine.AndroidJNI::SetBooleanField(System.IntPtr,System.IntPtr,System.Boolean)` | 引擎+游戏 | impl=0x175E48 |
| `UnityEngine.AndroidJNI::SetCharArrayElement(System.IntPtr,System.Int32,System.Char)` | 引擎+游戏 | impl=0x176F34 |
| `UnityEngine.AndroidJNI::SetCharField(System.IntPtr,System.IntPtr,System.Char)` | 引擎+游戏 | impl=0x175E58 |
| `UnityEngine.AndroidJNI::SetDoubleArrayElement(System.IntPtr,System.Int32,System.Double)` | 引擎+游戏 | impl=0x176F48 |
| `UnityEngine.AndroidJNI::SetDoubleField(System.IntPtr,System.IntPtr,System.Double)` | 引擎+游戏 | impl=0x175E6C |
| `UnityEngine.AndroidJNI::SetFloatArrayElement(System.IntPtr,System.Int32,System.Single)` | 引擎+游戏 | impl=0x176F44 |
| `UnityEngine.AndroidJNI::SetFloatField(System.IntPtr,System.IntPtr,System.Single)` | 引擎+游戏 | impl=0x175E68 |
| `UnityEngine.AndroidJNI::SetIntArrayElement(System.IntPtr,System.Int32,System.Int32)` | 引擎+游戏 | impl=0x176F3C |
| `UnityEngine.AndroidJNI::SetIntField(System.IntPtr,System.IntPtr,System.Int32)` | 引擎+游戏 | impl=0x175E60 |
| `UnityEngine.AndroidJNI::SetLongArrayElement(System.IntPtr,System.Int32,System.Int64)` | 引擎+游戏 | impl=0x176F40 |
| `UnityEngine.AndroidJNI::SetLongField(System.IntPtr,System.IntPtr,System.Int64)` | 引擎+游戏 | impl=0x175E64 |
| `UnityEngine.AndroidJNI::SetObjectArrayElement(System.IntPtr,System.Int32,System.IntPtr)` | 引擎+游戏 | impl=0x176F4C |
| `UnityEngine.AndroidJNI::SetObjectField(System.IntPtr,System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x175E44 |
| `UnityEngine.AndroidJNI::SetSByteArrayElement(System.IntPtr,System.Int32,System.SByte)` | 引擎+游戏 | impl=0x176F30 |
| `UnityEngine.AndroidJNI::SetSByteField(System.IntPtr,System.IntPtr,System.SByte)` | 引擎+游戏 | impl=0x175E54 |
| `UnityEngine.AndroidJNI::SetShortArrayElement(System.IntPtr,System.Int32,System.Int16)` | 引擎+游戏 | impl=0x176F38 |
| `UnityEngine.AndroidJNI::SetShortField(System.IntPtr,System.IntPtr,System.Int16)` | 引擎+游戏 | impl=0x175E5C |
| `UnityEngine.AndroidJNI::SetStaticBooleanField(System.IntPtr,System.IntPtr,System.Boolean)` | 引擎+游戏 | impl=0x176630 |
| `UnityEngine.AndroidJNI::SetStaticCharField(System.IntPtr,System.IntPtr,System.Char)` | 引擎+游戏 | impl=0x176640 |
| `UnityEngine.AndroidJNI::SetStaticDoubleField(System.IntPtr,System.IntPtr,System.Double)` | 引擎+游戏 | impl=0x176654 |
| `UnityEngine.AndroidJNI::SetStaticFloatField(System.IntPtr,System.IntPtr,System.Single)` | 引擎+游戏 | impl=0x176650 |
| `UnityEngine.AndroidJNI::SetStaticIntField(System.IntPtr,System.IntPtr,System.Int32)` | 引擎+游戏 | impl=0x176648 |
| `UnityEngine.AndroidJNI::SetStaticLongField(System.IntPtr,System.IntPtr,System.Int64)` | 引擎+游戏 | impl=0x17664C |
| `UnityEngine.AndroidJNI::SetStaticObjectField(System.IntPtr,System.IntPtr,System.IntPtr)` | 引擎+游戏 | impl=0x17662C |
| `UnityEngine.AndroidJNI::SetStaticSByteField(System.IntPtr,System.IntPtr,System.SByte)` | 引擎+游戏 | impl=0x17663C |
| `UnityEngine.AndroidJNI::SetStaticShortField(System.IntPtr,System.IntPtr,System.Int16)` | 引擎+游戏 | impl=0x176644 |
| `UnityEngine.AndroidJNI::SetStaticStringField(System.IntPtr,System.IntPtr,System.String)` | 引擎+游戏 | impl=0x176590 |
| `UnityEngine.AndroidJNI::SetStringField(System.IntPtr,System.IntPtr,System.String)` | 引擎+游戏 | impl=0x175DA8 |
| `UnityEngine.AndroidJNI::Throw(System.IntPtr)` | 引擎+游戏 | impl=0x174CC8 |
| `UnityEngine.AndroidJNI::ThrowNew(System.IntPtr,System.String)` | 引擎+游戏 | impl=0x174CCC |
| `UnityEngine.AndroidJNI::ToBooleanArray(System.Boolean[])` | 引擎+游戏 | impl=0x176658 |
| `UnityEngine.AndroidJNI::ToByteArray(System.Byte[])` | 引擎+游戏 | impl=0x1766E4 |
| `UnityEngine.AndroidJNI::ToCharArray(System.Char[])` | 引擎+游戏 | impl=0x1767FC |
| `UnityEngine.AndroidJNI::ToDoubleArray(System.Double[])` | 引擎+游戏 | impl=0x176AB8 |
| `UnityEngine.AndroidJNI::ToFloatArray(System.Single[])` | 引擎+游戏 | impl=0x176A2C |
| `UnityEngine.AndroidJNI::ToIntArray(System.Int32[])` | 引擎+游戏 | impl=0x176914 |
| `UnityEngine.AndroidJNI::ToLongArray(System.Int64[])` | 引擎+游戏 | impl=0x1769A0 |
| `UnityEngine.AndroidJNI::ToObjectArray(System.IntPtr[],System.IntPtr)` | 引擎+游戏 | impl=0x176B44 |
| `UnityEngine.AndroidJNI::ToReflectedField(System.IntPtr,System.IntPtr,System.Boolean)` | 引擎+游戏 | impl=0x174CA4 |
| `UnityEngine.AndroidJNI::ToReflectedMethod(System.IntPtr,System.IntPtr,System.Boolean)` | 引擎+游戏 | impl=0x174C98 |
| `UnityEngine.AndroidJNI::ToSByteArray(System.SByte[])` | 引擎+游戏 | impl=0x176770 |
| `UnityEngine.AndroidJNI::ToShortArray(System.Int16[])` | 引擎+游戏 | impl=0x176888 |
| `UnityEngine.AndroidJNIHelper::get_debug()` | 引擎+游戏 | impl=0x174B90 |
| `UnityEngine.AndroidJNIHelper::set_debug(System.Boolean)` | 引擎+游戏 | impl=0x174B98 |
| `UnityEngine.AsyncOperation::InternalDestroy(System.IntPtr)` | 引擎+游戏 | impl=0x39D0DC |
| `UnityEngine.AsyncOperation::get_isDone()` | 引擎+游戏 | impl=0x39D0E0 |
| `UnityEngine.AsyncOperation::get_progress()` | 引擎+游戏 | impl=0x39D168 |
| `UnityEngine.Camera::GetAllCamerasCount()` | 引擎+游戏 | impl=0x394588 |
| `UnityEngine.Camera::GetAllCamerasImpl(UnityEngine.Camera[])` | 引擎+游戏 | impl=0x39458C |
| `UnityEngine.Camera::Render()` | 引擎+游戏 | impl=0x3947C4 |
| `UnityEngine.Camera::ScreenPointToRay_Injected(UnityEngine.Vector2&,UnityEngine.Camera/MonoOrStereoscopicEye,UnityEngine.Ray&)` | 引擎+游戏 | impl=0x394440 |
| `UnityEngine.Camera::ScreenToViewportPoint_Injected(UnityEngine.Vector3&,UnityEngine.Vector3&)` | 引擎+游戏 | impl=0x394330 |
| `UnityEngine.Camera::ScreenToWorldPoint_Injected(UnityEngine.Vector3&,UnityEngine.Camera/MonoOrStereoscopicEye,UnityEngine.Vector3&)` | 引擎+游戏 | impl=0x39421C |
| `UnityEngine.Camera::WorldToScreenPoint_Injected(UnityEngine.Vector3&,UnityEngine.Camera/MonoOrStereoscopicEye,UnityEngine.Vector3&)` | 引擎+游戏 | impl=0x394104 |
| `UnityEngine.Camera::get_aspect()` | 引擎+游戏 | impl=0x393104 |
| `UnityEngine.Camera::get_backgroundColor_Injected(UnityEngine.Color&)` | 引擎+游戏 | impl=0x39349C |
| `UnityEngine.Camera::get_clearFlags()` | 引擎+游戏 | impl=0x393674 |
| `UnityEngine.Camera::get_cullingMask()` | 引擎+游戏 | impl=0x3932DC |
| `UnityEngine.Camera::get_current()` | 引擎+游戏 | impl=0x39456C |
| `UnityEngine.Camera::get_depth()` | 引擎+游戏 | impl=0x393024 |
| `UnityEngine.Camera::get_eventMask()` | 引擎+游戏 | impl=0x3933BC |
| `UnityEngine.Camera::get_farClipPlane()` | 引擎+游戏 | impl=0x3928D0 |
| `UnityEngine.Camera::get_fieldOfView()` | 引擎+游戏 | impl=0x392AA8 |
| `UnityEngine.Camera::get_main()` | 引擎+游戏 | impl=0x394554 |
| `UnityEngine.Camera::get_nearClipPlane()` | 引擎+游戏 | impl=0x3926F8 |
| `UnityEngine.Camera::get_orthographic()` | 引擎+游戏 | impl=0x392E54 |
| `UnityEngine.Camera::get_orthographicSize()` | 引擎+游戏 | impl=0x392C80 |
| `UnityEngine.Camera::get_pixelHeight()` | 引擎+游戏 | impl=0x393C00 |
| `UnityEngine.Camera::get_pixelRect_Injected(UnityEngine.Rect&)` | 引擎+游戏 | impl=0x393938 |
| `UnityEngine.Camera::get_pixelWidth()` | 引擎+游戏 | impl=0x393B1C |
| `UnityEngine.Camera::get_rect_Injected(UnityEngine.Rect&)` | 引擎+游戏 | impl=0x393754 |
| `UnityEngine.Camera::get_targetDisplay()` | 引擎+游戏 | impl=0x394020 |
| `UnityEngine.Camera::set_aspect(System.Single)` | 引擎+游戏 | impl=0x3931E8 |
| `UnityEngine.Camera::set_backgroundColor_Injected(UnityEngine.Color&)` | 引擎+游戏 | impl=0x393588 |
| `UnityEngine.Camera::set_farClipPlane(System.Single)` | 引擎+游戏 | impl=0x3929B4 |
| `UnityEngine.Camera::set_fieldOfView(System.Single)` | 引擎+游戏 | impl=0x392B8C |
| `UnityEngine.Camera::set_nearClipPlane(System.Single)` | 引擎+游戏 | impl=0x3927DC |
| `UnityEngine.Camera::set_orthographic(System.Boolean)` | 引擎+游戏 | impl=0x392F34 |
| `UnityEngine.Camera::set_orthographicSize(System.Single)` | 引擎+游戏 | impl=0x392D60 |
| `UnityEngine.Camera::set_pixelRect_Injected(UnityEngine.Rect&)` | 引擎+游戏 | impl=0x393A30 |
| `UnityEngine.Camera::set_rect_Injected(UnityEngine.Rect&)` | 引擎+游戏 | impl=0x39384C |
| `UnityEngine.CameraRaycastHelper::RaycastTry2D_Injected(UnityEngine.Camera,UnityEngine.Ray&,System.Single,System.Int32)` | 引擎+游戏 | impl=0x1790B8 |
| `UnityEngine.CameraRaycastHelper::RaycastTry_Injected(UnityEngine.Camera,UnityEngine.Ray&,System.Single,System.Int32)` | 引擎+游戏 | impl=0x178FF8 |
| `UnityEngine.ComputeShader::FindKernel(System.String)` | 引擎+游戏 | impl=0x3A0BBC |
| `UnityEngine.ContactFilter2D::CheckConsistency_Injected(UnityEngine.ContactFilter2D&)` | 引擎+游戏 | impl=0x5F9E28 |
| `UnityEngine.Coroutine::ReleaseCoroutine(System.IntPtr)` | 引擎+游戏 | impl=0x39DAB4 |
| `UnityEngine.Cursor::get_lockState()` | 引擎+游戏 | impl=0x39C0E8 |
| `UnityEngine.Debug::Break()` | 引擎+游戏 | impl=0x394BA4 |
| `UnityEngine.Debug::DrawLine_Injected(UnityEngine.Vector3&,UnityEngine.Vector3&,UnityEngine.Color&,System.Single,System.Boolean)` | 引擎+游戏 | impl=0x394BA0 |
| `UnityEngine.Debug::ExtractStackTraceNoAlloc(System.Byte*,System.Int32,System.String)` | 引擎+游戏 | impl=0x394BA8 |
| `UnityEngine.Debug::get_isDebugBuild()` | 引擎+游戏 | impl=0x394C2C |
| `UnityEngine.DebugLogHandler::Internal_Log(UnityEngine.LogType,UnityEngine.LogOption,System.String,UnityEngine.Object)` | 引擎+游戏 | impl=0x3948A8 |
| `UnityEngine.DebugLogHandler::Internal_LogException(System.Exception,UnityEngine.Object)` | 引擎+游戏 | impl=0x394A98 |
| `UnityEngine.Display::GetRenderingExtImpl(System.IntPtr,System.Int32&,System.Int32&)` | 引擎+游戏 | impl=0x39536C |
| `UnityEngine.Display::GetSystemExtImpl(System.IntPtr,System.Int32&,System.Int32&)` | 引擎+游戏 | impl=0x395368 |
| `UnityEngine.Display::RelativeMouseAtImpl(System.Int32,System.Int32,System.Int32&,System.Int32&)` | 引擎+游戏 | impl=0x395370 |
| `UnityEngine.Event::GetTypeForControl(System.Int32)` | 引擎+游戏 | impl=0x1818B0 |
| `UnityEngine.Event::Internal_Create(System.Int32)` | 引擎+游戏 | impl=0x181824 |
| `UnityEngine.Event::Internal_Destroy(System.IntPtr)` | 引擎+游戏 | impl=0x181880 |
| `UnityEngine.Event::Internal_SetNativeEvent(System.IntPtr)` | 引擎+游戏 | impl=0x1819D8 |
| `UnityEngine.Event::Internal_Use()` | 引擎+游戏 | impl=0x1817A4 |
| `UnityEngine.Event::PopEvent(UnityEngine.Event)` | 引擎+游戏 | impl=0x181948 |
| `UnityEngine.Event::get_character()` | 引擎+游戏 | impl=0x1812EC |
| `UnityEngine.Event::get_clickCount()` | 引擎+游戏 | impl=0x181270 |
| `UnityEngine.Event::get_commandName()` | 引擎+游戏 | impl=0x181698 |
| `UnityEngine.Event::get_delta_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x181064 |
| `UnityEngine.Event::get_keyCode()` | 引擎+游戏 | impl=0x1813F0 |
| `UnityEngine.Event::get_modifiers()` | 引擎+游戏 | impl=0x18116C |
| `UnityEngine.Event::get_mousePosition_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x180FD8 |
| `UnityEngine.Event::get_pointerType()` | 引擎+游戏 | impl=0x1810F0 |
| `UnityEngine.Event::get_rawType()` | 引擎+游戏 | impl=0x180F5C |
| `UnityEngine.Event::get_type()` | 引擎+游戏 | impl=0x18157C |
| `UnityEngine.Event::set_character(System.Char)` | 引擎+游戏 | impl=0x181368 |
| `UnityEngine.Event::set_displayIndex(System.Int32)` | 引擎+游戏 | impl=0x1814F4 |
| `UnityEngine.Event::set_keyCode(UnityEngine.KeyCode)` | 引擎+游戏 | impl=0x18146C |
| `UnityEngine.Event::set_modifiers(UnityEngine.EventModifiers)` | 引擎+游戏 | impl=0x1811E8 |
| `UnityEngine.Event::set_type(UnityEngine.EventType)` | 引擎+游戏 | impl=0x18160C |
| `UnityEngine.Experimental.Rendering.BuiltinRuntimeReflectionSystem::BuiltinUpdate()` | 引擎+游戏 | impl=0x3A526C |
| `UnityEngine.Experimental.Rendering.ScriptableRuntimeReflectionSystemSettings::ScriptingDirtyReflectionSystemInstance()` | 引擎+游戏 | impl=0x3A5284 |
| `UnityEngine.Gizmos::DrawLine_Injected(UnityEngine.Vector3&,UnityEngine.Vector3&)` | 引擎+游戏 | impl=0x3952B8 |
| `UnityEngine.Gizmos::DrawSphere_Injected(UnityEngine.Vector3&,System.Single)` | 引擎+游戏 | impl=0x3952C0 |
| `UnityEngine.Gizmos::DrawWireCube_Injected(UnityEngine.Vector3&,UnityEngine.Vector3&)` | 引擎+游戏 | impl=0x3952C4 |
| `UnityEngine.Gizmos::DrawWireSphere_Injected(UnityEngine.Vector3&,System.Single)` | 引擎+游戏 | impl=0x3952BC |
| `UnityEngine.Gizmos::get_color_Injected(UnityEngine.Color&)` | 引擎+游戏 | impl=0x3952C8 |
| `UnityEngine.Gizmos::set_color_Injected(UnityEngine.Color&)` | 引擎+游戏 | impl=0x395350 |
| `UnityEngine.Gradient::Cleanup()` | 引擎+游戏 | impl=0x39C1C4 |
| `UnityEngine.Gradient::Init()` | 引擎+游戏 | impl=0x39C1C0 |
| `UnityEngine.Gradient::Internal_Equals(System.IntPtr)` | 引擎+游戏 | impl=0x39C240 |
| `UnityEngine.Gradient::get_colorKeys()` | 引擎+游戏 | impl=0x39C2D0 |
| `UnityEngine.Light::get_bounceIntensity()` | 引擎+游戏 | impl=0x398A44 |
| `UnityEngine.Light::get_color_Injected(UnityEngine.Color&)` | 引擎+游戏 | impl=0x398694 |
| `UnityEngine.Light::get_intensity()` | 引擎+游戏 | impl=0x39886C |
| `UnityEngine.Light::get_range()` | 引擎+游戏 | impl=0x398B28 |
| `UnityEngine.Light::get_shadowStrength()` | 引擎+游戏 | impl=0x398D08 |
| `UnityEngine.Light::get_shadows()` | 引擎+游戏 | impl=0x398C24 |
| `UnityEngine.Light::get_spotAngle()` | 引擎+游戏 | impl=0x3985B0 |
| `UnityEngine.Light::get_type()` | 引擎+游戏 | impl=0x3984CC |
| `UnityEngine.Light::set_color_Injected(UnityEngine.Color&)` | 引擎+游戏 | impl=0x398780 |
| `UnityEngine.Light::set_intensity(System.Single)` | 引擎+游戏 | impl=0x398950 |
| `UnityEngine.Light::set_shadowStrength(System.Single)` | 引擎+游戏 | impl=0x398DEC |
| `UnityEngine.LineRenderer::set_endColor_Injected(UnityEngine.Color&)` | 引擎+游戏 | impl=0x395ECC |
| `UnityEngine.LineRenderer::set_startColor_Injected(UnityEngine.Color&)` | 引擎+游戏 | impl=0x395D84 |
| `UnityEngine.Material::ComputeCRC()` | 引擎+游戏 | impl=0x39798C |
| `UnityEngine.Material::CopyPropertiesFromMaterial(UnityEngine.Material)` | 引擎+游戏 | impl=0x3975A8 |
| `UnityEngine.Material::CreateWithMaterial(UnityEngine.Material,UnityEngine.Material)` | 引擎+游戏 | impl=0x396D14 |
| `UnityEngine.Material::CreateWithShader(UnityEngine.Material,UnityEngine.Shader)` | 引擎+游戏 | impl=0x396BD4 |
| `UnityEngine.Material::CreateWithString(UnityEngine.Material)` | 引擎+游戏 | impl=0x396E54 |
| `UnityEngine.Material::DisableKeyword(System.String)` | 引擎+游戏 | impl=0x397334 |
| `UnityEngine.Material::EnableKeyword(System.String)` | 引擎+游戏 | impl=0x3971A4 |
| `UnityEngine.Material::GetColorImpl_Injected(System.Int32,UnityEngine.Color&)` | 引擎+游戏 | impl=0x397FC8 |
| `UnityEngine.Material::GetFirstPropertyNameIdByAttribute(UnityEngine.Rendering.ShaderPropertyFlags)` | 引擎+游戏 | impl=0x396EDC |
| `UnityEngine.Material::GetFloatImpl(System.Int32)` | 引擎+游戏 | impl=0x397EDC |
| `UnityEngine.Material::GetShaderKeywords()` | 引擎+游戏 | impl=0x397710 |
| `UnityEngine.Material::HasProperty(System.Int32)` | 引擎+游戏 | impl=0x396FC8 |
| `UnityEngine.Material::SetColorImpl_Injected(System.Int32,UnityEngine.Color&)` | 引擎+游戏 | impl=0x397B6C |
| `UnityEngine.Material::SetFloatImpl(System.Int32,System.Single)` | 引擎+游戏 | impl=0x397A70 |
| `UnityEngine.Material::SetMatrixImpl_Injected(System.Int32,UnityEngine.Matrix4x4&)` | 引擎+游戏 | impl=0x397C68 |
| `UnityEngine.Material::SetShaderKeywords(System.String[])` | 引擎+游戏 | impl=0x397828 |
| `UnityEngine.Material::get_passCount()` | 引擎+游戏 | impl=0x3974C4 |
| `UnityEngine.Material::set_renderQueue(System.Int32)` | 引擎+游戏 | impl=0x3970B8 |
| `UnityEngine.Mathf::GammaToLinearSpace(System.Single)` | 引擎+游戏 | impl=0x39C6EC |
| `UnityEngine.Mathf::NextPowerOfTwo(System.Int32)` | 引擎+游戏 | impl=0x39C6CC |
| `UnityEngine.Mathf::PerlinNoise(System.Single,System.Single)` | 引擎+游戏 | impl=0x39C754 |
| `UnityEngine.Matrix4x4::GetLossyScale_Injected(UnityEngine.Matrix4x4&,UnityEngine.Vector3&)` | 引擎+游戏 | impl=0x39C34C |
| `UnityEngine.Matrix4x4::TRS_Injected(UnityEngine.Vector3&,UnityEngine.Quaternion&,UnityEngine.Vector3&,UnityEngine.Matrix4x4&)` | 引擎+游戏 | impl=0x39C37C |
| `UnityEngine.NoAllocHelpers::ExtractArrayFromList(System.Object)` | 引擎+游戏 | impl=0x39FE20 |
| `UnityEngine.Playables.PlayableHandle::GetPlayableType_Injected(UnityEngine.Playables.PlayableHandle&)` | 引擎+游戏 | impl=0x3A5238 |
| `UnityEngine.Playables.PlayableHandle::IsValid_Injected(UnityEngine.Playables.PlayableHandle&)` | 引擎+游戏 | impl=0x3A5210 |
| `UnityEngine.PlayerConnectionInternal::DisconnectAll()` | 引擎+游戏 | impl=0x39CAB8 |
| `UnityEngine.PlayerConnectionInternal::Initialize()` | 引擎+游戏 | impl=0x39C784 |
| `UnityEngine.PlayerConnectionInternal::IsConnected()` | 引擎+游戏 | impl=0x39C77C |
| `UnityEngine.PlayerConnectionInternal::PollInternal()` | 引擎+游戏 | impl=0x39CAB4 |
| `UnityEngine.PlayerConnectionInternal::RegisterInternal(System.String)` | 引擎+游戏 | impl=0x39C788 |
| `UnityEngine.PlayerConnectionInternal::SendMessage(System.String,System.Byte[],System.Int32)` | 引擎+游戏 | impl=0x39C8D0 |
| `UnityEngine.PlayerConnectionInternal::TrySendMessage(System.String,System.Byte[],System.Int32)` | 引擎+游戏 | impl=0x39C9C0 |
| `UnityEngine.PlayerConnectionInternal::UnregisterInternal(System.String)` | 引擎+游戏 | impl=0x39C82C |
| `UnityEngine.PlayerPrefs::GetString(System.String,System.String)` | 引擎+游戏 | impl=0x39CBE4 |
| `UnityEngine.PlayerPrefs::TrySetSetString(System.String,System.String)` | 引擎+游戏 | impl=0x39CABC |
| `UnityEngine.QualitySettings::get_activeColorSpace()` | 引擎+游戏 | impl=0x395788 |
| `UnityEngine.QualitySettings::set_vSyncCount(System.Int32)` | 引擎+游戏 | impl=0x39576C |
| `UnityEngine.Quaternion::AngleAxis_Injected(System.Single,UnityEngine.Vector3&,UnityEngine.Quaternion&)` | 引擎+游戏 | impl=0x39C4D4 |
| `UnityEngine.Quaternion::Internal_FromEulerRad_Injected(UnityEngine.Vector3&,UnityEngine.Quaternion&)` | 引擎+游戏 | impl=0x39C408 |
| `UnityEngine.Quaternion::Internal_ToEulerRad_Injected(UnityEngine.Quaternion&,UnityEngine.Vector3&)` | 引擎+游戏 | impl=0x39C428 |
| `UnityEngine.Quaternion::Inverse_Injected(UnityEngine.Quaternion&,UnityEngine.Quaternion&)` | 引擎+游戏 | impl=0x39C3B4 |
| `UnityEngine.Quaternion::LookRotation_Injected(UnityEngine.Vector3&,UnityEngine.Vector3&,UnityEngine.Quaternion&)` | 引擎+游戏 | impl=0x39C5AC |
| `UnityEngine.Quaternion::Slerp_Injected(UnityEngine.Quaternion&,UnityEngine.Quaternion&,System.Single,UnityEngine.Quaternion&)` | 引擎+游戏 | impl=0x39C3D8 |
| `UnityEngine.Random::RandomRangeInt(System.Int32,System.Int32)` | 引擎+游戏 | impl=0x39CDA0 |
| `UnityEngine.Random::Range(System.Single,System.Single)` | 引擎+游戏 | impl=0x39CD34 |
| `UnityEngine.Random::get_value()` | 引擎+游戏 | impl=0x39CDC8 |
| `UnityEngine.RectOffset::InternalCreate()` | 引擎+游戏 | impl=0x394C40 |
| `UnityEngine.RectOffset::InternalDestroy(System.IntPtr)` | 引擎+游戏 | impl=0x394C6C |
| `UnityEngine.RectOffset::Remove_Injected(UnityEngine.Rect&,UnityEngine.Rect&)` | 引擎+游戏 | impl=0x3951DC |
| `UnityEngine.RectOffset::get_bottom()` | 引擎+游戏 | impl=0x394F94 |
| `UnityEngine.RectOffset::get_horizontal()` | 引擎+游戏 | impl=0x395098 |
| `UnityEngine.RectOffset::get_left()` | 引擎+游戏 | impl=0x394C88 |
| `UnityEngine.RectOffset::get_right()` | 引擎+游戏 | impl=0x394D8C |
| `UnityEngine.RectOffset::get_top()` | 引擎+游戏 | impl=0x394E90 |
| `UnityEngine.RectOffset::get_vertical()` | 引擎+游戏 | impl=0x395118 |
| `UnityEngine.RectOffset::set_bottom(System.Int32)` | 引擎+游戏 | impl=0x395010 |
| `UnityEngine.RectOffset::set_left(System.Int32)` | 引擎+游戏 | impl=0x394D04 |
| `UnityEngine.RectOffset::set_right(System.Int32)` | 引擎+游戏 | impl=0x394E08 |
| `UnityEngine.RectOffset::set_top(System.Int32)` | 引擎+游戏 | impl=0x394F0C |
| `UnityEngine.Renderer::GetMaterial()` | 引擎+游戏 | impl=0x395FDC |
| `UnityEngine.Renderer::GetSharedMaterial()` | 引擎+游戏 | impl=0x3960C8 |
| `UnityEngine.Renderer::SetMaterial(UnityEngine.Material)` | 引擎+游戏 | impl=0x3961B4 |
| `UnityEngine.Renderer::get_enabled()` | 引擎+游戏 | impl=0x39631C |
| `UnityEngine.Renderer::get_sortingLayerID()` | 引擎+游戏 | impl=0x3966DC |
| `UnityEngine.Renderer::get_sortingOrder()` | 引擎+游戏 | impl=0x3968AC |
| `UnityEngine.Renderer::set_enabled(System.Boolean)` | 引擎+游戏 | impl=0x396408 |
| `UnityEngine.Renderer::set_receiveShadows(System.Boolean)` | 引擎+游戏 | impl=0x3965E8 |
| `UnityEngine.Renderer::set_shadowCastingMode(UnityEngine.Rendering.ShadowCastingMode)` | 引擎+游戏 | impl=0x3964FC |
| `UnityEngine.Renderer::set_sortingLayerID(System.Int32)` | 引擎+游戏 | impl=0x3967C0 |
| `UnityEngine.Renderer::set_sortingOrder(System.Int32)` | 引擎+游戏 | impl=0x39698C |
| `UnityEngine.Rendering.GraphicsSettings::AllowEnlightenSupportForUpgradedProject()` | 引擎+游戏 | impl=0x3A51D8 |
| `UnityEngine.Rendering.GraphicsSettings::get_lightsUseLinearIntensity()` | 引擎+游戏 | impl=0x3A51C4 |
| `UnityEngine.Rendering.ScriptableRenderContext::GetCamera_Internal_Injected(UnityEngine.Rendering.ScriptableRenderContext&,System.Int32)` | 引擎+游戏 | impl=0x3A51F4 |
| `UnityEngine.Rendering.ScriptableRenderContext::GetNumberOfCameras_Internal_Injected(UnityEngine.Rendering.ScriptableRenderContext&)` | 引擎+游戏 | impl=0x3A51EC |
| `UnityEngine.ScreenCapture::CaptureScreenshot(System.String,System.Int32,UnityEngine.ScreenCapture/StereoScreenCaptureMode)` | 引擎+游戏 | impl=0x17CD48 |
| `UnityEngine.Shader::Find(System.String)` | 引擎+游戏 | impl=0x396A78 |
| `UnityEngine.Shader::PropertyToID(System.String)` | 引擎+游戏 | impl=0x396B48 |
| `UnityEngine.SortingLayer::GetLayerValueFromID(System.Int32)` | 引擎+游戏 | impl=0x392188 |
| `UnityEngine.TouchScreenKeyboard::GetSelection(System.Int32&,System.Int32&)` | 引擎+游戏 | impl=0x3A14F8 |
| `UnityEngine.TouchScreenKeyboard::Internal_Destroy(System.IntPtr)` | 引擎+游戏 | impl=0x3A0E08 |
| `UnityEngine.TouchScreenKeyboard::IsInPlaceEditingAllowed()` | 引擎+游戏 | impl=0x3A0FB4 |
| `UnityEngine.TouchScreenKeyboard::IsRequiredToForceOpen()` | 引擎+游戏 | impl=0x3A0FC8 |
| `UnityEngine.TouchScreenKeyboard::SetSelection(System.Int32,System.Int32)` | 引擎+游戏 | impl=0x3A14FC |
| `UnityEngine.TouchScreenKeyboard::TouchScreenKeyboard_InternalConstructorHelper(UnityEngine.TouchScreenKeyboard_InternalConstructorHelperArguments&,System.String,System.String)` | 引擎+游戏 | impl=0x3A0E38 |
| `UnityEngine.TouchScreenKeyboard::get_active()` | 引擎+游戏 | impl=0x3A11E0 |
| `UnityEngine.TouchScreenKeyboard::get_canGetSelection()` | 引擎+游戏 | impl=0x3A13F8 |
| `UnityEngine.TouchScreenKeyboard::get_canSetSelection()` | 引擎+游戏 | impl=0x3A1478 |
| `UnityEngine.TouchScreenKeyboard::get_status()` | 引擎+游戏 | impl=0x3A12F0 |
| `UnityEngine.TouchScreenKeyboard::get_text()` | 引擎+游戏 | impl=0x3A0FD0 |
| `UnityEngine.TouchScreenKeyboard::set_active(System.Boolean)` | 引擎+游戏 | impl=0x3A1260 |
| `UnityEngine.TouchScreenKeyboard::set_characterLimit(System.Int32)` | 引擎+游戏 | impl=0x3A136C |
| `UnityEngine.TouchScreenKeyboard::set_text(System.String)` | 引擎+游戏 | impl=0x3A10A4 |
| `UnityEngine.TrailRenderer::get_endWidth()` | 引擎+游戏 | impl=0x395B70 |
| `UnityEngine.TrailRenderer::get_startWidth()` | 引擎+游戏 | impl=0x395964 |
| `UnityEngine.TrailRenderer::get_time()` | 引擎+游戏 | impl=0x395798 |
| `UnityEngine.TrailRenderer::set_endWidth(System.Single)` | 引擎+游戏 | impl=0x395C78 |
| `UnityEngine.TrailRenderer::set_startWidth(System.Single)` | 引擎+游戏 | impl=0x395A54 |
| `UnityEngine.TrailRenderer::set_time(System.Single)` | 引擎+游戏 | impl=0x395878 |
| `UnityEngine.UISystemProfilerApi::AddMarker(System.String,UnityEngine.Object)` | 引擎+游戏 | impl=0x18083C |
| `UnityEngine.UISystemProfilerApi::BeginSample(UnityEngine.UISystemProfilerApi/SampleType)` | 引擎+游戏 | impl=0x180834 |
| `UnityEngine.UISystemProfilerApi::EndSample(UnityEngine.UISystemProfilerApi/SampleType)` | 引擎+游戏 | impl=0x180838 |
| `UnityEngine.UnityLogWriter::WriteStringToUnityLogImpl(System.String)` | 引擎+游戏 | impl=0x39C0FC |

## 其他（190）

| ICall | 侧 | libunity impl |
|---|---|---|
| `ActivationServices::EnableProxyActivation` | 游戏(libil2cpp) | — |
| `AppDomain::createDomain` | 游戏(libil2cpp) | — |
| `AppDomain::ExecuteAssembly` | 游戏(libil2cpp) | — |
| `AppDomain::InternalGetProcessGuid` | 游戏(libil2cpp) | — |
| `AppDomain::InternalPopDomainRef` | 游戏(libil2cpp) | — |
| `AppDomain::InternalPushDomainRef` | 游戏(libil2cpp) | — |
| `AppDomain::InternalPushDomainRefByID` | 游戏(libil2cpp) | — |
| `AppDomain::InternalSetContext` | 游戏(libil2cpp) | — |
| `AppDomain::InternalSetDomain` | 游戏(libil2cpp) | — |
| `AppDomain::InternalSetDomainByID` | 游戏(libil2cpp) | — |
| `AppDomain::InternalUnload` | 游戏(libil2cpp) | — |
| `AppDomain::LoadAssemblyRaw` | 游戏(libil2cpp) | — |
| `ArgIterator::IntGetNextArg` | 游戏(libil2cpp) | — |
| `ArgIterator::Setup` | 游戏(libil2cpp) | — |
| `Assembly::GetManifestModuleInternal` | 游戏(libil2cpp) | — |
| `Assembly::GetNamespaces` | 游戏(libil2cpp) | — |
| `Assembly::InternalGetAssemblyName` | 游戏(libil2cpp) | — |
| `Assembly::InternalImageRuntimeVersion` | 游戏(libil2cpp) | — |
| `Assembly::MonoDebugger_GetMethodToken` | 游戏(libil2cpp) | — |
| `AssemblyBuilder::basic_init` | 游戏(libil2cpp) | — |
| `AssemblyBuilder::InternalAddModule` | 游戏(libil2cpp) | — |
| `CameraScripting::GetAllCameras` | 游戏(libil2cpp) | — |
| `CameraScripting::GetAllCamerasCount` | 游戏(libil2cpp) | — |
| `CameraScripting::GetPixelHeight` | 游戏(libil2cpp) | — |
| `CameraScripting::GetPixelWidth` | 游戏(libil2cpp) | — |
| `CameraScripting::RaycastTry` | 游戏(libil2cpp) | — |
| `CameraScripting::RaycastTry2D` | 游戏(libil2cpp) | — |
| `CameraScripting::Render` | 游戏(libil2cpp) | — |
| `ComputeShaderScripting::FindKernel` | 游戏(libil2cpp) | — |
| `condition_variable::timed` | 游戏(libil2cpp) | — |
| `condition_variable::wait` | 游戏(libil2cpp) | — |
| `Coroutine::CleanupCoroutineGC` | 游戏(libil2cpp) | — |
| `CultureInfo::internal_is_lcid_neutral` | 游戏(libil2cpp) | — |
| `CustomAttributeBuilder::GetBlob` | 游戏(libil2cpp) | — |
| `CustomAttributeData::ResolveArgumentsInternal` | 游戏(libil2cpp) | — |
| `DerivedType::create_unmanaged_type` | 游戏(libil2cpp) | — |
| `DriveInfo::GetDiskFreeSpaceInternal` | 游戏(libil2cpp) | — |
| `DriveInfo::GetDriveTypeInternal` | 游戏(libil2cpp) | — |
| `DynamicMethod::create_dynamic_method` | 游戏(libil2cpp) | — |
| `DynamicMethod::destroy_dynamic_method` | 游戏(libil2cpp) | — |
| `EnumBuilder::setup_enum_type` | 游戏(libil2cpp) | — |
| `Evidence::IsAuthenticodePresent` | 游戏(libil2cpp) | — |
| `FieldInfo::GetTypeModifiers` | 游戏(libil2cpp) | — |
| `GCHandle::NewWeakref` | 游戏(libil2cpp) | — |
| `GenericTypeParameterBuilder::initialize` | 游戏(libil2cpp) | — |
| `Gradient_Bindings::Cleanup` | 游戏(libil2cpp) | — |
| `Gradient_Bindings::GetColorKeys` | 游戏(libil2cpp) | — |
| `Gradient_Bindings::Init` | 游戏(libil2cpp) | — |
| `Gradient_Bindings::Internal_Equals` | 游戏(libil2cpp) | — |
| `GraphicsScripting::BlitMaterial` | 游戏(libil2cpp) | — |
| `ios_base::clear` | 游戏(libil2cpp) | — |
| `Light_Bindings::SetShadowStrength` | 游戏(libil2cpp) | — |
| `Marshal::GetComSlotForMethodInfoInternal` | 游戏(libil2cpp) | — |
| `Marshal::GetIDispatchForObjectInternal` | 游戏(libil2cpp) | — |
| `Marshal::GetIUnknownForObjectInternal` | 游戏(libil2cpp) | — |
| `Marshal::Prelink` | 游戏(libil2cpp) | — |
| `Marshal::PrelinkAll` | 游戏(libil2cpp) | — |
| `MaterialScripting::CopyPropertiesFrom` | 游戏(libil2cpp) | — |
| `MaterialScripting::CreateWithMaterial` | 游戏(libil2cpp) | — |
| `MaterialScripting::CreateWithShader` | 游戏(libil2cpp) | — |
| `MaterialScripting::CreateWithString` | 游戏(libil2cpp) | — |
| `MaterialScripting::GetShaderKeywords` | 游戏(libil2cpp) | — |
| `MaterialScripting::SetShaderKeywords` | 游戏(libil2cpp) | — |
| `MatrixScripting::TRS` | 游戏(libil2cpp) | — |
| `MemberInfo::get_MetadataToken` | 游戏(libil2cpp) | — |
| `MethodBase::GetCurrentMethod` | 游戏(libil2cpp) | — |
| `MethodBase::GetMethodBodyInternal` | 游戏(libil2cpp) | — |
| `MethodBuilder::MakeGenericMethod` | 游戏(libil2cpp) | — |
| `Module::GetGlobalType` | 游戏(libil2cpp) | — |
| `Module::GetMDStreamVersion` | 游戏(libil2cpp) | — |
| `Module::GetPEKind` | 游戏(libil2cpp) | — |
| `Module::ResolveFieldToken` | 游戏(libil2cpp) | — |
| `Module::ResolveMemberToken` | 游戏(libil2cpp) | — |
| `Module::ResolveMethodToken` | 游戏(libil2cpp) | — |
| `Module::ResolveSignature` | 游戏(libil2cpp) | — |
| `Module::ResolveStringToken` | 游戏(libil2cpp) | — |
| `Module::ResolveTypeToken` | 游戏(libil2cpp) | — |
| `ModuleBuilder::basic_init` | 游戏(libil2cpp) | — |
| `ModuleBuilder::build_metadata` | 游戏(libil2cpp) | — |
| `ModuleBuilder::create_modified_type` | 游戏(libil2cpp) | — |
| `ModuleBuilder::getMethodToken` | 游戏(libil2cpp) | — |
| `ModuleBuilder::getToken` | 游戏(libil2cpp) | — |
| `ModuleBuilder::getUSIndex` | 游戏(libil2cpp) | — |
| `ModuleBuilder::RegisterToken` | 游戏(libil2cpp) | — |
| `ModuleBuilder::set_wrappers_type` | 游戏(libil2cpp) | — |
| `ModuleBuilder::WriteToFile` | 游戏(libil2cpp) | — |
| `Mono.Runtime::mono_runtime_cleanup_handlers()` | 游戏(libil2cpp) | — |
| `MonoGenericCMethod::get_ReflectedType` | 游戏(libil2cpp) | — |
| `MonoGenericMethod::get_ReflectedType` | 游戏(libil2cpp) | — |
| `MonoPropertyInfo::GetTypeModifiers` | 游戏(libil2cpp) | — |
| `MonoType::GetCorrespondingInflatedConstructor` | 游戏(libil2cpp) | — |
| `MonoType::GetCorrespondingInflatedMethod` | 游戏(libil2cpp) | — |
| `MonoType::type_from_obj` | 游戏(libil2cpp) | — |
| `Mutex::CreateMutex_internal` | 游戏(libil2cpp) | — |
| `NativeEventCalls::CreateEvent_internal` | 游戏(libil2cpp) | — |
| `NoAllocHelpers_Bindings::ExtractArrayFromList` | 游戏(libil2cpp) | — |
| `ParameterInfo::GetTypeModifiers` | 游戏(libil2cpp) | — |
| `PerformanceCounterCategory::Create` | 游戏(libil2cpp) | — |
| `PerlinNoise::NoiseNormalized` | 游戏(libil2cpp) | — |
| `PlayableHandleBindings::GetPlayableType` | 游戏(libil2cpp) | — |
| `PlayerConnection_Bindings::DisconnectAll` | 游戏(libil2cpp) | — |
| `PlayerConnection_Bindings::Initialize` | 游戏(libil2cpp) | — |
| `PlayerConnection_Bindings::IsConnected` | 游戏(libil2cpp) | — |
| `PlayerConnection_Bindings::PollInternal` | 游戏(libil2cpp) | — |
| `PlayerConnection_Bindings::RegisterInternal` | 游戏(libil2cpp) | — |
| `PlayerConnection_Bindings::SendMessage` | 游戏(libil2cpp) | — |
| `PlayerConnection_Bindings::TrySendMessage` | 游戏(libil2cpp) | — |
| `PlayerConnection_Bindings::UnregisterInternal` | 游戏(libil2cpp) | — |
| `Process::GetProcessName` | 游戏(libil2cpp) | — |
| `QuaternionScripting::AngleAxis` | 游戏(libil2cpp) | — |
| `QuaternionScripting::LookRotation` | 游戏(libil2cpp) | — |
| `QuaternionScripting::Slerp` | 游戏(libil2cpp) | — |
| `QuaternionScripting::ToEuler` | 游戏(libil2cpp) | — |
| `RealProxy::InternalGetProxyType` | 游戏(libil2cpp) | — |
| `RealProxy::InternalGetTransparentProxy` | 游戏(libil2cpp) | — |
| `RemotingServices::GetVirtualMethod` | 游戏(libil2cpp) | — |
| `RemotingServices::InternalExecute` | 游戏(libil2cpp) | — |
| `RendererScripting::GetMaterial` | 游戏(libil2cpp) | — |
| `RendererScripting::GetSharedMaterial` | 游戏(libil2cpp) | — |
| `RendererScripting::SetMaterial` | 游戏(libil2cpp) | — |
| `Runtime::mono_runtime_install_handlers` | 游戏(libil2cpp) | — |
| `RuntimeHelpers::RunModuleConstructor` | 游戏(libil2cpp) | — |
| `ScreenScripting::GetResolutions` | 游戏(libil2cpp) | — |
| `Scripting::DestroyObjectFromScripting` | 游戏(libil2cpp) | — |
| `Scripting::DestroyObjectFromScriptingImmediate` | 游戏(libil2cpp) | — |
| `Scripting::SendScriptingMessage` | 游戏(libil2cpp) | — |
| `ScriptingGraphicsCaps::GetCompatibleFormat` | 游戏(libil2cpp) | — |
| `ScriptingGraphicsCaps::IsFormatSupported` | 游戏(libil2cpp) | — |
| `SecurityFrame::_GetSecurityFrame` | 游戏(libil2cpp) | — |
| `SecurityManager::set_CheckExecutionRights` | 游戏(libil2cpp) | — |
| `SecurityManager::set_SecurityEnabled` | 游戏(libil2cpp) | — |
| `Semaphore::CreateSemaphore_internal` | 游戏(libil2cpp) | — |
| `Semaphore::OpenSemaphore_internal` | 游戏(libil2cpp) | — |
| `ShaderScripting::PropertyToID` | 游戏(libil2cpp) | — |
| `SignatureHelper::get_signature_field` | 游戏(libil2cpp) | — |
| `SignatureHelper::get_signature_local` | 游戏(libil2cpp) | — |
| `std::__libcpp_tls_set` | 游戏(libil2cpp) | — |
| `std::allocator` | 游戏(libil2cpp) | — |
| `std::bad_alloc` | 游戏(libil2cpp) | — |
| `std::bad_cast` | 游戏(libil2cpp) | — |
| `std::bad_exception` | 游戏(libil2cpp) | — |
| `std::bad_typeid` | 游戏(libil2cpp) | — |
| `std::basic_iostream` | 游戏(libil2cpp) | — |
| `std::basic_istream` | 游戏(libil2cpp) | — |
| `std::basic_ostream` | 游戏(libil2cpp) | — |
| `std::basic_string` | 游戏(libil2cpp) | — |
| `std::char_traits` | 游戏(libil2cpp) | — |
| `std::exception` | 游戏(libil2cpp) | — |
| `std::iostream` | 游戏(libil2cpp) | — |
| `std::istream` | 游戏(libil2cpp) | — |
| `std::nullptr_t` | 游戏(libil2cpp) | — |
| `std::ostream` | 游戏(libil2cpp) | — |
| `std::string` | 游戏(libil2cpp) | — |
| `string_view::substr` | 游戏(libil2cpp) | — |
| `String::RedirectToCreateString` | 游戏(libil2cpp) | — |
| `systeminfo::GetOperatingSystemFamily` | 游戏(libil2cpp) | — |
| `systeminfo::GetRuntimePlatform` | 游戏(libil2cpp) | — |
| `systeminfo::GetSystemLanguage` | 游戏(libil2cpp) | — |
| `thread::detach` | 游戏(libil2cpp) | — |
| `Thread::GetAbortExceptionState` | 游戏(libil2cpp) | — |
| `thread::join` | 游戏(libil2cpp) | — |
| `Thread::Resume_internal` | 游戏(libil2cpp) | — |
| `Thread::Suspend_internal` | 游戏(libil2cpp) | — |
| `Thread::SuspendInternal` | 游戏(libil2cpp) | — |
| `ThreadPool::GetConfiguration` | 游戏(libil2cpp) | — |
| `ThreadPool::Initialize` | 游戏(libil2cpp) | — |
| `ThreadPool::Queue` | 游戏(libil2cpp) | — |
| `ThreadPool::SetConfiguration` | 游戏(libil2cpp) | — |
| `ThreadPool::Shutdown` | 游戏(libil2cpp) | — |
| `ThreadPool::Wait` | 游戏(libil2cpp) | — |
| `TypeBuilder::create_generic_class` | 游戏(libil2cpp) | — |
| `TypeBuilder::create_internal_class` | 游戏(libil2cpp) | — |
| `TypeBuilder::create_runtime_class` | 游戏(libil2cpp) | — |
| `TypeBuilder::get_event_info` | 游戏(libil2cpp) | — |
| `TypeBuilder::get_IsGenericParameter` | 游戏(libil2cpp) | — |
| `TypeBuilder::setup_generic_class` | 游戏(libil2cpp) | — |
| `TypeBuilder::setup_internal_class` | 游戏(libil2cpp) | — |
| `TypedReference::ToObject` | 游戏(libil2cpp) | — |
| `UI::GetDefaultUIMaterial` | 游戏(libil2cpp) | — |
| `UI::SystemProfilerApi` | 游戏(libil2cpp) | — |
| `unique_lock::unlock` | 游戏(libil2cpp) | — |
| `vm::ThreadPool` | 游戏(libil2cpp) | — |
| `WindowsIdentity::_GetRoles` | 游戏(libil2cpp) | — |
| `WindowsIdentity::GetUserToken` | 游戏(libil2cpp) | — |
| `WindowsImpersonationContext::CloseToken` | 游戏(libil2cpp) | — |
| `WindowsImpersonationContext::DuplicateToken` | 游戏(libil2cpp) | — |
| `WindowsImpersonationContext::RevertToSelf` | 游戏(libil2cpp) | — |
| `WindowsImpersonationContext::SetCurrentToken` | 游戏(libil2cpp) | — |
| `WindowsPrincipal::IsMemberOfGroupId` | 游戏(libil2cpp) | — |
| `WindowsPrincipal::IsMemberOfGroupName` | 游戏(libil2cpp) | — |

## IMGUI / GUI / 字体（119）

| ICall | 侧 | libunity impl |
|---|---|---|
| `UnityEngine.Font::HasCharacter(System.Int32)` | 引擎+游戏 | impl=0x60E9B4 |
| `UnityEngine.Font::Internal_CreateFont(UnityEngine.Font,System.String)` | 引擎+游戏 | impl=0x60EAA4 |
| `UnityEngine.Font::get_dynamic()` | 引擎+游戏 | impl=0x60E7EC |
| `UnityEngine.Font::get_fontSize()` | 引擎+游戏 | impl=0x60E8D4 |
| `UnityEngine.Font::get_material()` | 引擎+游戏 | impl=0x60E6F4 |
| `UnityEngine.GUI::GrabMouseControl(System.Int32)` | 引擎+游戏 | impl=0x181A18 |
| `UnityEngine.GUI::HasMouseControl(System.Int32)` | 引擎+游戏 | impl=0x181A68 |
| `UnityEngine.GUI::InternalRepaintEditorWindow()` | 引擎+游戏 | impl=0x181ABC |
| `UnityEngine.GUI::ReleaseMouseControl()` | 引擎+游戏 | impl=0x181A8C |
| `UnityEngine.GUI::get_usePageScrollbars()` | 引擎+游戏 | impl=0x181A10 |
| `UnityEngine.GUI::set_changed(System.Boolean)` | 引擎+游戏 | impl=0x1819F4 |
| `UnityEngine.GUIClip::Internal_Pop()` | 引擎+游戏 | impl=0x181AE8 |
| `UnityEngine.GUIClip::Internal_Push_Injected(UnityEngine.Rect&,UnityEngine.Vector2&,UnityEngine.Vector2&,System.Boolean)` | 引擎+游戏 | impl=0x181ADC |
| `UnityEngine.GUIClip::SetMatrix_Injected(UnityEngine.Matrix4x4&)` | 引擎+游戏 | impl=0x181AEC |
| `UnityEngine.GUIClip::get_visibleRect_Injected(UnityEngine.Rect&)` | 引擎+游戏 | impl=0x181AC0 |
| `UnityEngine.GUILayoutUtility::Internal_GetWindowRect_Injected(System.Int32,UnityEngine.Rect&)` | 引擎+游戏 | impl=0x181AF0 |
| `UnityEngine.GUILayoutUtility::Internal_MoveWindow_Injected(System.Int32,UnityEngine.Rect&)` | 引擎+游戏 | impl=0x181B20 |
| `UnityEngine.GUISettings::Internal_GetCursorFlashSpeed()` | 引擎+游戏 | impl=0x181B48 |
| `UnityEngine.GUIStyle::GetRectOffsetPtr(System.Int32)` | 引擎+游戏 | impl=0x182768 |
| `UnityEngine.GUIStyle::GetStyleStatePtr(System.Int32)` | 引擎+游戏 | impl=0x1826D8 |
| `UnityEngine.GUIStyle::Internal_CalcHeight(UnityEngine.GUIContent,System.Single)` | 引擎+游戏 | impl=0x183180 |
| `UnityEngine.GUIStyle::Internal_CalcMinMaxWidth_Injected(UnityEngine.GUIContent,UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x183258 |
| `UnityEngine.GUIStyle::Internal_CalcSizeWithConstraints_Injected(UnityEngine.GUIContent,UnityEngine.Vector2&,UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x183098 |
| `UnityEngine.GUIStyle::Internal_CalcSize_Injected(UnityEngine.GUIContent,UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x182FBC |
| `UnityEngine.GUIStyle::Internal_Copy(UnityEngine.GUIStyle,UnityEngine.GUIStyle)` | 引擎+游戏 | impl=0x182624 |
| `UnityEngine.GUIStyle::Internal_Create(UnityEngine.GUIStyle)` | 引擎+游戏 | impl=0x1825C8 |
| `UnityEngine.GUIStyle::Internal_Destroy(System.IntPtr)` | 引擎+游戏 | impl=0x1826B8 |
| `UnityEngine.GUIStyle::Internal_Draw2_Injected(UnityEngine.Rect&,UnityEngine.GUIContent,System.Int32,System.Boolean)` | 引擎+游戏 | impl=0x182930 |
| `UnityEngine.GUIStyle::Internal_DrawCursor_Injected(UnityEngine.Rect&,UnityEngine.GUIContent,System.Int32,UnityEngine.Color&)` | 引擎+游戏 | impl=0x182A38 |
| `UnityEngine.GUIStyle::Internal_DrawWithTextSelection_Injected(UnityEngine.Rect&,UnityEngine.GUIContent,System.Boolean,System.Boolean,System.Boolean,System.Boolean,System.Boolean,System.Int32,System.Int32,UnityEngine.Color&,UnityEngine.Color&)` | 引擎+游戏 | impl=0x182B38 |
| `UnityEngine.GUIStyle::Internal_Draw_Injected(UnityEngine.Rect&,UnityEngine.GUIContent,System.Boolean,System.Boolean,System.Boolean,System.Boolean)` | 引擎+游戏 | impl=0x1827F8 |
| `UnityEngine.GUIStyle::Internal_GetCursorFlashOffset()` | 引擎+游戏 | impl=0x183430 |
| `UnityEngine.GUIStyle::Internal_GetCursorPixelPosition_Injected(UnityEngine.Rect&,UnityEngine.GUIContent,System.Int32,UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x182CA0 |
| `UnityEngine.GUIStyle::Internal_GetCursorStringIndex_Injected(UnityEngine.Rect&,UnityEngine.GUIContent,UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x182D94 |
| `UnityEngine.GUIStyle::Internal_GetLineHeight(System.IntPtr)` | 引擎+游戏 | impl=0x1827F4 |
| `UnityEngine.GUIStyle::Internal_GetSelectedRenderedText_Injected(UnityEngine.Rect&,UnityEngine.GUIContent,System.Int32,System.Int32)` | 引擎+游戏 | impl=0x182E74 |
| `UnityEngine.GUIStyle::SetDefaultFont(UnityEngine.Font)` | 引擎+游戏 | impl=0x183440 |
| `UnityEngine.GUIStyle::SetMouseTooltip_Injected(System.String,UnityEngine.Rect&)` | 引擎+游戏 | impl=0x183340 |
| `UnityEngine.GUIStyle::get_contentOffset_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x182108 |
| `UnityEngine.GUIStyle::get_fixedHeight()` | 引擎+游戏 | impl=0x1822A4 |
| `UnityEngine.GUIStyle::get_fixedWidth()` | 引擎+游戏 | impl=0x182228 |
| `UnityEngine.GUIStyle::get_font()` | 引擎+游戏 | impl=0x181F80 |
| `UnityEngine.GUIStyle::get_imagePosition()` | 引擎+游戏 | impl=0x182010 |
| `UnityEngine.GUIStyle::get_rawName()` | 引擎+游戏 | impl=0x181D68 |
| `UnityEngine.GUIStyle::get_stretchHeight()` | 引擎+游戏 | impl=0x18242C |
| `UnityEngine.GUIStyle::get_stretchWidth()` | 引擎+游戏 | impl=0x182320 |
| `UnityEngine.GUIStyle::get_wordWrap()` | 引擎+游戏 | impl=0x18208C |
| `UnityEngine.GUIStyle::set_Internal_clipOffset_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x182538 |
| `UnityEngine.GUIStyle::set_contentOffset_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x182198 |
| `UnityEngine.GUIStyle::set_rawName(System.String)` | 引擎+游戏 | impl=0x181E50 |
| `UnityEngine.GUIStyle::set_stretchHeight(System.Boolean)` | 引擎+游戏 | impl=0x1824A8 |
| `UnityEngine.GUIStyle::set_stretchWidth(System.Boolean)` | 引擎+游戏 | impl=0x18239C |
| `UnityEngine.GUIStyleState::Cleanup()` | 引擎+游戏 | impl=0x181CEC |
| `UnityEngine.GUIStyleState::Init()` | 引擎+游戏 | impl=0x181CCC |
| `UnityEngine.GUIStyleState::set_textColor_Injected(UnityEngine.Color&)` | 引擎+游戏 | impl=0x181C40 |
| `UnityEngine.GUIUtility::GetControlID_Injected(System.Int32,UnityEngine.FocusType,UnityEngine.Rect&)` | 引擎+游戏 | impl=0x18364C |
| `UnityEngine.GUIUtility::Internal_ExitGUI()` | 引擎+游戏 | impl=0x183738 |
| `UnityEngine.GUIUtility::Internal_GetDefaultSkin(System.Int32)` | 引擎+游戏 | impl=0x183734 |
| `UnityEngine.GUIUtility::Internal_GetHotControl()` | 引擎+游戏 | impl=0x183724 |
| `UnityEngine.GUIUtility::Internal_GetKeyboardControl()` | 引擎+游戏 | impl=0x183728 |
| `UnityEngine.GUIUtility::Internal_SetHotControl(System.Int32)` | 引擎+游戏 | impl=0x18372C |
| `UnityEngine.GUIUtility::Internal_SetKeyboardControl(System.Int32)` | 引擎+游戏 | impl=0x183730 |
| `UnityEngine.GUIUtility::get_compositionString()` | 引擎+游戏 | impl=0x18367C |
| `UnityEngine.GUIUtility::get_guiDepth()` | 引擎+游戏 | impl=0x1834E4 |
| `UnityEngine.GUIUtility::get_pixelsPerPoint()` | 引擎+游戏 | impl=0x1834D0 |
| `UnityEngine.GUIUtility::get_systemCopyBuffer()` | 引擎+游戏 | impl=0x183530 |
| `UnityEngine.GUIUtility::set_compositionCursorPos_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x183708 |
| `UnityEngine.GUIUtility::set_mouseUsed(System.Boolean)` | 引擎+游戏 | impl=0x1834F8 |
| `UnityEngine.GUIUtility::set_systemCopyBuffer(System.String)` | 引擎+游戏 | impl=0x1835A0 |
| `UnityEngine.GUIUtility::set_textFieldInput(System.Boolean)` | 引擎+游戏 | impl=0x183510 |
| `UnityEngine.MonoBehaviour::get_useGUILayout()` | 引擎+游戏 | impl=0x39F0DC |
| `UnityEngine.MonoBehaviour::set_useGUILayout(System.Boolean)` | 引擎+游戏 | impl=0x39F1C4 |
| `UnityEngine.TextCore.LowLevel.FontEngine::GetFaceInfo_Internal(UnityEngine.TextCore.FaceInfo&)` | 引擎+游戏 | impl=0x60EDB4 |
| `UnityEngine.TextCore.LowLevel.FontEngine::GetGlyphIndex(System.UInt32)` | 引擎+游戏 | impl=0x60EFB0 |
| `UnityEngine.TextCore.LowLevel.FontEngine::GetGlyphPairAdjustmentRecordsFromMarshallingArray(UnityEngine.TextCore.LowLevel.GlyphPairAdjustmentRecord[])` | 引擎+游戏 | impl=0x60F910 |
| `UnityEngine.TextCore.LowLevel.FontEngine::InitializeFontEngine_Internal()` | 引擎+游戏 | impl=0x60ED18 |
| `UnityEngine.TextCore.LowLevel.FontEngine::LoadFontFace_With_Size_FromFont_Internal(UnityEngine.Font,System.Int32)` | 引擎+游戏 | impl=0x60ED1C |
| `UnityEngine.TextCore.LowLevel.FontEngine::PopulatePairAdjustmentRecordMarshallingArray_from_GlyphIndexes(System.UInt32[],System.Int32&)` | 引擎+游戏 | impl=0x60F87C |
| `UnityEngine.TextCore.LowLevel.FontEngine::TryGetGlyphWithIndexValue_Internal(System.UInt32,UnityEngine.TextCore.LowLevel.GlyphLoadFlags,UnityEngine.TextCore.LowLevel.GlyphMarshallingStruct&)` | 引擎+游戏 | impl=0x60EFC8 |
| `UnityEngine.TextCore.LowLevel.FontEngine::TryGetGlyphWithUnicodeValue_Internal(System.UInt32,UnityEngine.TextCore.LowLevel.GlyphLoadFlags,UnityEngine.TextCore.LowLevel.GlyphMarshallingStruct&)` | 引擎+游戏 | impl=0x60EFB4 |
| `UnityEngine.TextGenerator::GetCharactersInternal(System.Object)` | 引擎+游戏 | impl=0x60E54C |
| `UnityEngine.TextGenerator::GetLinesInternal(System.Object)` | 引擎+游戏 | impl=0x60E620 |
| `UnityEngine.TextGenerator::GetVerticesInternal(System.Object)` | 引擎+游戏 | impl=0x60E478 |
| `UnityEngine.TextGenerator::Internal_Create()` | 引擎+游戏 | impl=0x60E030 |
| `UnityEngine.TextGenerator::Internal_Destroy(System.IntPtr)` | 引擎+游戏 | impl=0x60E078 |
| `UnityEngine.TextGenerator::Populate_Internal_Injected(System.String,UnityEngine.Font,UnityEngine.Color&,System.Int32,System.Single,System.Single,UnityEngine.FontStyle,System.Boolean,System.Boolean,System.Int32,System.Int32,System.Int32,System.Int32,System.Boolean,UnityEngine.TextAnchor,System.Single,System.Single,System.Single,System.Single,System.Boolean,System.Boolean,System.UInt32&)` | 引擎+游戏 | impl=0x60E0AC |
| `UnityEngine.TextGenerator::get_characterCount()` | 引擎+游戏 | impl=0x60DF18 |
| `UnityEngine.TextGenerator::get_lineCount()` | 引擎+游戏 | impl=0x60DFA4 |
| `UnityEngine.TextGenerator::get_rectExtents_Injected(UnityEngine.Rect&)` | 引擎+游戏 | impl=0x60DE8C |
| `GUIEvent::GetCommandName` | 游戏(libil2cpp) | — |
| `GUIEvent::GetType` | 游戏(libil2cpp) | — |
| `GUIEvent::GetTypeForControl` | 游戏(libil2cpp) | — |
| `GUIEvent::Internal_Create` | 游戏(libil2cpp) | — |
| `GUIEvent::Internal_Destroy` | 游戏(libil2cpp) | — |
| `GUIEvent::SetType` | 游戏(libil2cpp) | — |
| `GUIStyle_Bindings::GetRectOffsetPtr` | 游戏(libil2cpp) | — |
| `GUIStyle_Bindings::GetStyleStatePtr` | 游戏(libil2cpp) | — |
| `GUIStyle_Bindings::Internal_CalcHeight` | 游戏(libil2cpp) | — |
| `GUIStyle_Bindings::Internal_CalcMinMaxWidth` | 游戏(libil2cpp) | — |
| `GUIStyle_Bindings::Internal_CalcSize` | 游戏(libil2cpp) | — |
| `GUIStyle_Bindings::Internal_CalcSizeWithConstraints` | 游戏(libil2cpp) | — |
| `GUIStyle_Bindings::Internal_Copy` | 游戏(libil2cpp) | — |
| `GUIStyle_Bindings::Internal_Create` | 游戏(libil2cpp) | — |
| `GUIStyle_Bindings::Internal_Destroy` | 游戏(libil2cpp) | — |
| `GUIStyle_Bindings::Internal_Draw` | 游戏(libil2cpp) | — |
| `GUIStyle_Bindings::Internal_Draw2` | 游戏(libil2cpp) | — |
| `GUIStyle_Bindings::Internal_DrawCursor` | 游戏(libil2cpp) | — |
| `GUIStyle_Bindings::Internal_DrawWithTextSelection` | 游戏(libil2cpp) | — |
| `GUIStyle_Bindings::Internal_GetCursorFlashOffset` | 游戏(libil2cpp) | — |
| `GUIStyle_Bindings::Internal_GetCursorPixelPosition` | 游戏(libil2cpp) | — |
| `GUIStyle_Bindings::Internal_GetCursorStringIndex` | 游戏(libil2cpp) | — |
| `GUIStyle_Bindings::Internal_GetLineHeight` | 游戏(libil2cpp) | — |
| `GUIStyle_Bindings::Internal_GetSelectedRenderedText` | 游戏(libil2cpp) | — |
| `GUIStyle_Bindings::SetMouseTooltip` | 游戏(libil2cpp) | — |
| `GUIStyle::SetDefaultFont` | 游戏(libil2cpp) | — |
| `GUIStyleState_Bindings::Cleanup` | 游戏(libil2cpp) | — |
| `GUIStyleState_Bindings::Init` | 游戏(libil2cpp) | — |
| `TextCore::FontEngine` | 游戏(libil2cpp) | — |
| `TextRendering::Font` | 游戏(libil2cpp) | — |

## 纹理 / 精灵 / 图片（117）

| ICall | 侧 | libunity impl |
|---|---|---|
| `UnityEngine.Camera::get_activeTexture()` | 引擎+游戏 | impl=0x393F38 |
| `UnityEngine.Camera::get_targetTexture()` | 引擎+游戏 | impl=0x393CE4 |
| `UnityEngine.Camera::set_targetTexture(UnityEngine.RenderTexture)` | 引擎+游戏 | impl=0x393DD0 |
| `UnityEngine.CanvasRenderer::SetAlphaTexture(UnityEngine.Texture)` | 引擎+游戏 | impl=0x17E88C |
| `UnityEngine.CanvasRenderer::SetTexture(UnityEngine.Texture)` | 引擎+游戏 | impl=0x17E724 |
| `UnityEngine.Cubemap::Internal_CreateImpl(UnityEngine.Cubemap,System.Int32,System.Int32,UnityEngine.Experimental.Rendering.GraphicsFormat,UnityEngine.Experimental.Rendering.TextureCreationFlags,System.IntPtr)` | 引擎+游戏 | impl=0x39ADD0 |
| `UnityEngine.Cubemap::get_isReadable()` | 引擎+游戏 | impl=0x39AE94 |
| `UnityEngine.CubemapArray::Internal_CreateImpl(UnityEngine.CubemapArray,System.Int32,System.Int32,System.Int32,UnityEngine.Experimental.Rendering.GraphicsFormat,UnityEngine.Experimental.Rendering.TextureCreationFlags)` | 引擎+游戏 | impl=0x39B3F0 |
| `UnityEngine.CubemapArray::get_isReadable()` | 引擎+游戏 | impl=0x39B304 |
| `UnityEngine.Experimental.Rendering.GraphicsFormatUtility::GetGraphicsFormat_Native_RenderTextureFormat(UnityEngine.RenderTextureFormat,System.Boolean)` | 引擎+游戏 | impl=0x3A5294 |
| `UnityEngine.Experimental.Rendering.GraphicsFormatUtility::GetGraphicsFormat_Native_TextureFormat(UnityEngine.TextureFormat,System.Boolean)` | 引擎+游戏 | impl=0x3A5288 |
| `UnityEngine.Experimental.Rendering.GraphicsFormatUtility::IsCompressedTextureFormat(UnityEngine.TextureFormat)` | 引擎+游戏 | impl=0x3A52B4 |
| `UnityEngine.Experimental.Rendering.GraphicsFormatUtility::IsSRGBFormat(UnityEngine.Experimental.Rendering.GraphicsFormat)` | 引擎+游戏 | impl=0x3A52A0 |
| `UnityEngine.GUIStyleState::set_background(UnityEngine.Texture2D)` | 引擎+游戏 | impl=0x181B50 |
| `UnityEngine.Graphics::Internal_BlitMaterial5(UnityEngine.Texture,UnityEngine.RenderTexture,UnityEngine.Material,System.Int32,System.Boolean)` | 引擎+游戏 | impl=0x395578 |
| `UnityEngine.ImageConversion::EncodeToJPG(UnityEngine.Texture2D,System.Int32)` | 引擎+游戏 | impl=0x51BFD8 |
| `UnityEngine.ImageConversion::EncodeToPNG(UnityEngine.Texture2D)` | 引擎+游戏 | impl=0x51BF14 |
| `UnityEngine.ImageConversion::LoadImage(UnityEngine.Texture2D,System.Byte[],System.Boolean)` | 引擎+游戏 | impl=0x51C0A0 |
| `UnityEngine.Material::GetTextureImpl(System.Int32)` | 引擎+游戏 | impl=0x3980C8 |
| `UnityEngine.Material::GetTextureScaleAndOffsetImpl_Injected(System.Int32,UnityEngine.Vector4&)` | 引擎+游戏 | impl=0x3981BC |
| `UnityEngine.Material::SetTextureImpl(System.Int32,UnityEngine.Texture)` | 引擎+游戏 | impl=0x397D64 |
| `UnityEngine.Material::SetTextureOffsetImpl_Injected(System.Int32,UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x3982C4 |
| `UnityEngine.Material::SetTextureScaleImpl_Injected(System.Int32,UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x3983C8 |
| `UnityEngine.Networking.DownloadHandlerTexture::Create(UnityEngine.Networking.DownloadHandlerTexture,System.Boolean)` | 引擎+游戏 | impl=0x180E34 |
| `UnityEngine.Networking.DownloadHandlerTexture::InternalGetTextureNative()` | 引擎+游戏 | impl=0x180E98 |
| `UnityEngine.ParticleSystem/TextureSheetAnimationModule::AddSprite_Injected` | 引擎(libunity) | impl=0x17B6A0 |
| `UnityEngine.ParticleSystem/TextureSheetAnimationModule::RemoveSprite_Injected` | 引擎(libunity) | impl=0x17B798 |
| `UnityEngine.ParticleSystem/TextureSheetAnimationModule::get_spriteCount_Injected` | 引擎(libunity) | impl=0x17B658 |
| `UnityEngine.ParticleSystem/TextureSheetAnimationModule::set_enabled_Injected` | 引擎(libunity) | impl=0x17B534 |
| `UnityEngine.ParticleSystem/TextureSheetAnimationModule::set_mode_Injected` | 引擎(libunity) | impl=0x17B5C4 |
| `UnityEngine.RenderTexture::DiscardContents(System.Boolean,System.Boolean)` | 引擎+游戏 | impl=0x39BB94 |
| `UnityEngine.RenderTexture::GetDescriptor_Injected(UnityEngine.RenderTextureDescriptor&)` | 引擎+游戏 | impl=0x39BEFC |
| `UnityEngine.RenderTexture::Internal_Create(UnityEngine.RenderTexture)` | 引擎+游戏 | impl=0x39BD88 |
| `UnityEngine.RenderTexture::SetActive(UnityEngine.RenderTexture)` | 引擎+游戏 | impl=0x39BB04 |
| `UnityEngine.RenderTexture::SetRenderTextureDescriptor_Injected(UnityEngine.RenderTextureDescriptor&)` | 引擎+游戏 | impl=0x39BE10 |
| `UnityEngine.RenderTexture::SetSRGBReadWrite(System.Boolean)` | 引擎+游戏 | impl=0x39BC98 |
| `UnityEngine.RenderTexture::get_antiAliasing()` | 引擎+游戏 | impl=0x39B938 |
| `UnityEngine.RenderTexture::get_height()` | 引擎+游戏 | impl=0x39B680 |
| `UnityEngine.RenderTexture::get_width()` | 引擎+游戏 | impl=0x39B4B4 |
| `UnityEngine.RenderTexture::set_antiAliasing(System.Int32)` | 引擎+游戏 | impl=0x39BA18 |
| `UnityEngine.RenderTexture::set_depth(System.Int32)` | 引擎+游戏 | impl=0x39BFFC |
| `UnityEngine.RenderTexture::set_graphicsFormat(UnityEngine.Experimental.Rendering.GraphicsFormat)` | 引擎+游戏 | impl=0x39B84C |
| `UnityEngine.RenderTexture::set_height(System.Int32)` | 引擎+游戏 | impl=0x39B760 |
| `UnityEngine.RenderTexture::set_width(System.Int32)` | 引擎+游戏 | impl=0x39B594 |
| `UnityEngine.Sprite::CreateSprite_Injected(UnityEngine.Texture2D,UnityEngine.Rect&,UnityEngine.Vector2&,System.Single,System.UInt32,UnityEngine.SpriteMeshType,UnityEngine.Vector4&,System.Boolean)` | 引擎+游戏 | impl=0x3A43D8 |
| `UnityEngine.Sprite::GetInnerUVs_Injected(UnityEngine.Vector4&)` | 引擎+游戏 | impl=0x3A40F0 |
| `UnityEngine.Sprite::GetOuterUVs_Injected(UnityEngine.Vector4&)` | 引擎+游戏 | impl=0x3A41E8 |
| `UnityEngine.Sprite::GetPacked()` | 引擎+游戏 | impl=0x3A3F0C |
| `UnityEngine.Sprite::GetPackingMode()` | 引擎+游戏 | impl=0x3A3E1C |
| `UnityEngine.Sprite::GetPadding_Injected(UnityEngine.Vector4&)` | 引擎+游戏 | impl=0x3A42E0 |
| `UnityEngine.Sprite::GetTextureRect_Injected(UnityEngine.Rect&)` | 引擎+游戏 | impl=0x3A3FFC |
| `UnityEngine.Sprite::get_associatedAlphaSplitTexture()` | 引擎+游戏 | impl=0x3A49B8 |
| `UnityEngine.Sprite::get_border_Injected(UnityEngine.Vector4&)` | 引擎+游戏 | impl=0x3A46EC |
| `UnityEngine.Sprite::get_bounds_Injected(UnityEngine.Bounds&)` | 引擎+游戏 | impl=0x3A44F8 |
| `UnityEngine.Sprite::get_pivot_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x3A4AB8 |
| `UnityEngine.Sprite::get_pixelsPerUnit()` | 引擎+游戏 | impl=0x3A48D8 |
| `UnityEngine.Sprite::get_rect_Injected(UnityEngine.Rect&)` | 引擎+游戏 | impl=0x3A4600 |
| `UnityEngine.Sprite::get_texture()` | 引擎+游戏 | impl=0x3A47D8 |
| `UnityEngine.Sprite::get_triangles()` | 引擎+游戏 | impl=0x3A4C8C |
| `UnityEngine.Sprite::get_uv()` | 引擎+游戏 | impl=0x3A4D70 |
| `UnityEngine.Sprite::get_vertices()` | 引擎+游戏 | impl=0x3A4BA8 |
| `UnityEngine.SpriteMask::get_sprite()` | 引擎+游戏 | impl=0x17CE20 |
| `UnityEngine.SpriteRenderer::get_color_Injected(UnityEngine.Color&)` | 引擎+游戏 | impl=0x3A3C44 |
| `UnityEngine.SpriteRenderer::set_color_Injected(UnityEngine.Color&)` | 引擎+游戏 | impl=0x3A3D30 |
| `UnityEngine.SystemInfo::GetCompatibleFormat(UnityEngine.Experimental.Rendering.GraphicsFormat,UnityEngine.Experimental.Rendering.FormatUsage)` | 引擎+游戏 | impl=0x3A0D58 |
| `UnityEngine.SystemInfo::GetGraphicsFormat(UnityEngine.Experimental.Rendering.DefaultFormat)` | 引擎+游戏 | impl=0x3A0D5C |
| `UnityEngine.SystemInfo::IsFormatSupported(UnityEngine.Experimental.Rendering.GraphicsFormat,UnityEngine.Experimental.Rendering.FormatUsage)` | 引擎+游戏 | impl=0x3A0D44 |
| `UnityEngine.SystemInfo::SupportsTextureFormatNative(UnityEngine.TextureFormat)` | 引擎+游戏 | impl=0x3A0D30 |
| `UnityEngine.TextCore.LowLevel.FontEngine::ResetAtlasTexture(UnityEngine.Texture2D)` | 引擎+游戏 | impl=0x60F990 |
| `UnityEngine.TextCore.LowLevel.FontEngine::TryAddGlyphToTexture_Internal(System.UInt32,System.Int32,UnityEngine.TextCore.LowLevel.GlyphPackingMode,UnityEngine.TextCore.GlyphRect[],System.Int32&,UnityEngine.TextCore.GlyphRect[],System.Int32&,UnityEngine.TextCore.LowLevel.GlyphRenderMode,UnityEngine.Texture2D,UnityEngine.TextCore.LowLevel.GlyphMarshallingStruct&)` | 引擎+游戏 | impl=0x60F2FC |
| `UnityEngine.TextCore.LowLevel.FontEngine::TryAddGlyphsToTexture_Internal(System.UInt32[],System.Int32,UnityEngine.TextCore.LowLevel.GlyphPackingMode,UnityEngine.TextCore.GlyphRect[],System.Int32&,UnityEngine.TextCore.GlyphRect[],System.Int32&,UnityEngine.TextCore.LowLevel.GlyphRenderMode,UnityEngine.Texture2D,UnityEngine.TextCore.LowLevel.GlyphMarshallingStruct[],System.Int32&)` | 引擎+游戏 | impl=0x60F4AC |
| `UnityEngine.Texture2D::ApplyImpl(System.Boolean,System.Boolean)` | 引擎+游戏 | impl=0x39A588 |
| `UnityEngine.Texture2D::GetPixelBilinearImpl_Injected(System.Int32,System.Single,System.Single,UnityEngine.Color&)` | 引擎+游戏 | impl=0x39A8D8 |
| `UnityEngine.Texture2D::Internal_CreateImpl(UnityEngine.Texture2D,System.Int32,System.Int32,System.Int32,UnityEngine.Experimental.Rendering.GraphicsFormat,UnityEngine.Experimental.Rendering.TextureCreationFlags,System.IntPtr)` | 引擎+游戏 | impl=0x39A3C8 |
| `UnityEngine.Texture2D::ReadPixelsImpl_Injected(UnityEngine.Rect&,System.Int32,System.Int32,System.Boolean)` | 引擎+游戏 | impl=0x39AB0C |
| `UnityEngine.Texture2D::ResizeImpl(System.Int32,System.Int32)` | 引擎+游戏 | impl=0x39A68C |
| `UnityEngine.Texture2D::ResizeWithFormatImpl(System.Int32,System.Int32,UnityEngine.TextureFormat,System.Boolean)` | 引擎+游戏 | impl=0x39A9F0 |
| `UnityEngine.Texture2D::SetPixelImpl_Injected(System.Int32,System.Int32,System.Int32,UnityEngine.Color&)` | 引擎+游戏 | impl=0x39A7C4 |
| `UnityEngine.Texture2D::SetPixelsImpl(System.Int32,System.Int32,System.Int32,System.Int32,UnityEngine.Color[],System.Int32,System.Int32)` | 引擎+游戏 | impl=0x39AC24 |
| `UnityEngine.Texture2D::get_isReadable()` | 引擎+游戏 | impl=0x39A49C |
| `UnityEngine.Texture2D::get_whiteTexture()` | 引擎+游戏 | impl=0x39A3B0 |
| `UnityEngine.Texture2DArray::Internal_CreateImpl(UnityEngine.Texture2DArray,System.Int32,System.Int32,System.Int32,System.Int32,UnityEngine.Experimental.Rendering.GraphicsFormat,UnityEngine.Experimental.Rendering.TextureCreationFlags)` | 引擎+游戏 | impl=0x39B230 |
| `UnityEngine.Texture2DArray::get_isReadable()` | 引擎+游戏 | impl=0x39B144 |
| `UnityEngine.Texture3D::Internal_CreateImpl(UnityEngine.Texture3D,System.Int32,System.Int32,System.Int32,System.Int32,UnityEngine.Experimental.Rendering.GraphicsFormat,UnityEngine.Experimental.Rendering.TextureCreationFlags)` | 引擎+游戏 | impl=0x39B070 |
| `UnityEngine.Texture3D::get_isReadable()` | 引擎+游戏 | impl=0x39AF84 |
| `UnityEngine.Texture::GetDataHeight()` | 引擎+游戏 | impl=0x39A004 |
| `UnityEngine.Texture::GetDataWidth()` | 引擎+游戏 | impl=0x399F1C |
| `UnityEngine.Texture::get_isReadable()` | 引擎+游戏 | impl=0x39A0EC |
| `UnityEngine.Texture::get_texelSize_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x39A2B8 |
| `UnityEngine.Texture::get_wrapMode()` | 引擎+游戏 | impl=0x39A1D8 |
| `UnityEngine.U2D.SpriteAtlas::CanBindTo(UnityEngine.Sprite)` | 引擎+游戏 | impl=0x3A4EF4 |
| `UnityEngine.U2D.SpriteAtlasManager::Register(UnityEngine.U2D.SpriteAtlas)` | 引擎+游戏 | impl=0x3A4E54 |
| `CubemapArrayScripting::Create` | 游戏(libil2cpp) | — |
| `CubemapScripting::Create` | 游戏(libil2cpp) | — |
| `ImageConversionBindings::EncodeToJPG` | 游戏(libil2cpp) | — |
| `ImageConversionBindings::EncodeToPNG` | 游戏(libil2cpp) | — |
| `ImageConversionBindings::LoadImage` | 游戏(libil2cpp) | — |
| `RenderTextureScripting::Create` | 游戏(libil2cpp) | — |
| `RenderTextureScripting::SetActive` | 游戏(libil2cpp) | — |
| `RenderTextureScripting::SetDepth` | 游戏(libil2cpp) | — |
| `ScriptingGraphicsCaps::GetGraphicsFormat` | 游戏(libil2cpp) | — |
| `ScriptingGraphicsCaps::SupportsTextureFormat` | 游戏(libil2cpp) | — |
| `SpriteAccessLegacy::GetSpriteIndices` | 游戏(libil2cpp) | — |
| `SpriteAccessLegacy::GetSpriteUVs` | 游戏(libil2cpp) | — |
| `SpriteAccessLegacy::GetSpriteVertices` | 游戏(libil2cpp) | — |
| `SpritesBindings::CreateSprite` | 游戏(libil2cpp) | — |
| `Texture2DArrayScripting::Create` | 游戏(libil2cpp) | — |
| `Texture2DScripting::Create` | 游戏(libil2cpp) | — |
| `Texture2DScripting::ReadPixels` | 游戏(libil2cpp) | — |
| `Texture2DScripting::ResizeWithFormat` | 游戏(libil2cpp) | — |
| `Texture2DScripting::SetPixels` | 游戏(libil2cpp) | — |
| `Texture3DScripting::Create` | 游戏(libil2cpp) | — |
| `TextureSheetAnimationModule::AddSprite_Injected(UnityEngine.ParticleSystem/TextureSheetAnimationModule&,UnityEngine.Sprite)` | 游戏(libil2cpp) | — |
| `TextureSheetAnimationModule::get_spriteCount_Injected(UnityEngine.ParticleSystem/TextureSheetAnimationModule&)` | 游戏(libil2cpp) | — |
| `TextureSheetAnimationModule::RemoveSprite_Injected(UnityEngine.ParticleSystem/TextureSheetAnimationModule&,System.Int32)` | 游戏(libil2cpp) | — |
| `TextureSheetAnimationModule::set_enabled_Injected(UnityEngine.ParticleSystem/TextureSheetAnimationModule&,System.Boolean)` | 游戏(libil2cpp) | — |
| `TextureSheetAnimationModule::set_mode_Injected(UnityEngine.ParticleSystem/TextureSheetAnimationModule&,UnityEngine.ParticleSystemAnimationMode)` | 游戏(libil2cpp) | — |

## 粒子 / 动画 / 物理（90）

| ICall | 侧 | libunity impl |
|---|---|---|
| `UnityEngine.Animation::GetState` | 引擎(libunity) | impl=0xC84B0 |
| `UnityEngine.Animation::GetStateAtIndex(System.Int32)` | 引擎+游戏 | impl=0xC8578 |
| `UnityEngine.Animation::GetStateCount()` | 引擎+游戏 | impl=0xC8618 |
| `UnityEngine.AnimationCurve::Evaluate(System.Single)` | 引擎+游戏 | impl=0x3922C0 |
| `UnityEngine.AnimationCurve::GetKey_Injected(System.Int32,UnityEngine.Keyframe&)` | 引擎+游戏 | impl=0x3923CC |
| `UnityEngine.AnimationCurve::Internal_Create(UnityEngine.Keyframe[])` | 引擎+游戏 | impl=0x3921A4 |
| `UnityEngine.AnimationCurve::Internal_Destroy(System.IntPtr)` | 引擎+游戏 | impl=0x3921A0 |
| `UnityEngine.AnimationCurve::Internal_Equals(System.IntPtr)` | 引擎+游戏 | impl=0x392230 |
| `UnityEngine.AnimationCurve::get_length()` | 引擎+游戏 | impl=0x392350 |
| `UnityEngine.AnimationState::set_speed(System.Single)` | 引擎+游戏 | impl=0xC867C |
| `UnityEngine.Animator::ResetTriggerString(System.String)` | 引擎+游戏 | impl=0x178BDC |
| `UnityEngine.Animator::SetTriggerString(System.String)` | 引擎+游戏 | impl=0x178A40 |
| `UnityEngine.Animator::get_hasBoundPlayables()` | 引擎+游戏 | impl=0x178958 |
| `UnityEngine.CanvasRenderer::SetMesh(UnityEngine.Mesh)` | 引擎+游戏 | impl=0x17E9F4 |
| `UnityEngine.CanvasRenderer::get_cullTransparentMesh()` | 引擎+游戏 | impl=0x17DAFC |
| `UnityEngine.CanvasRenderer::set_cullTransparentMesh(System.Boolean)` | 引擎+游戏 | impl=0x17DBDC |
| `UnityEngine.Graphics::Internal_GetMaxDrawMeshInstanceCount()` | 引擎+游戏 | impl=0x395504 |
| `UnityEngine.Mesh::ClearImpl(System.Boolean)` | 引擎+游戏 | impl=0x399C60 |
| `UnityEngine.Mesh::GetAllocArrayFromChannelImpl(UnityEngine.Rendering.VertexAttribute,UnityEngine.Rendering.VertexAttributeFormat,System.Int32)` | 引擎+游戏 | impl=0x39977C |
| `UnityEngine.Mesh::GetIndicesImpl(System.Int32,System.Boolean)` | 引擎+游戏 | impl=0x3991A8 |
| `UnityEngine.Mesh::HasVertexAttribute(UnityEngine.Rendering.VertexAttribute)` | 引擎+游戏 | impl=0x399540 |
| `UnityEngine.Mesh::Internal_Create(UnityEngine.Mesh)` | 引擎+游戏 | impl=0x399120 |
| `UnityEngine.Mesh::MarkDynamicImpl()` | 引擎+游戏 | impl=0x399E38 |
| `UnityEngine.Mesh::PrintErrorCantAccessChannel(UnityEngine.Rendering.VertexAttribute)` | 引擎+游戏 | impl=0x399454 |
| `UnityEngine.Mesh::RecalculateBoundsImpl()` | 引擎+游戏 | impl=0x399D50 |
| `UnityEngine.Mesh::SetArrayForChannelImpl(UnityEngine.Rendering.VertexAttribute,UnityEngine.Rendering.VertexAttributeFormat,System.Int32,System.Array,System.Int32,System.Int32,System.Int32)` | 引擎+游戏 | impl=0x399630 |
| `UnityEngine.Mesh::SetIndicesImpl(System.Int32,UnityEngine.MeshTopology,UnityEngine.Rendering.IndexFormat,System.Array,System.Int32,System.Int32,System.Boolean,System.Int32)` | 引擎+游戏 | impl=0x3992F4 |
| `UnityEngine.Mesh::get_bounds_Injected(UnityEngine.Bounds&)` | 引擎+游戏 | impl=0x399A80 |
| `UnityEngine.Mesh::get_canAccess()` | 引擎+游戏 | impl=0x3998B4 |
| `UnityEngine.Mesh::get_subMeshCount()` | 引擎+游戏 | impl=0x39999C |
| `UnityEngine.Mesh::set_bounds_Injected(UnityEngine.Bounds&)` | 引擎+游戏 | impl=0x399B74 |
| `UnityEngine.MeshFilter::set_sharedMesh(UnityEngine.Mesh)` | 引擎+游戏 | impl=0x398FC4 |
| `UnityEngine.ParticleSystem/EmissionModule::GetBurst_Injected` | 引擎(libunity) | impl=0x17B258 |
| `UnityEngine.ParticleSystem/EmissionModule::SetBurst_Injected` | 引擎(libunity) | impl=0x17B130 |
| `UnityEngine.ParticleSystem/EmissionModule::get_burstCount_Injected` | 引擎(libunity) | impl=0x17B3B0 |
| `UnityEngine.ParticleSystem/EmissionModule::set_burstCount_Injected` | 引擎(libunity) | impl=0x17B3F8 |
| `UnityEngine.ParticleSystem/EmissionModule::set_rateOverDistanceMultiplier_Injected` | 引擎(libunity) | impl=0x17B078 |
| `UnityEngine.ParticleSystem/EmissionModule::set_rateOverTimeMultiplier_Injected` | 引擎(libunity) | impl=0x17AFC0 |
| `UnityEngine.ParticleSystem/ExternalForcesModule::AddInfluence_Injected` | 引擎(libunity) | impl=0x17B9C8 |
| `UnityEngine.ParticleSystem/ExternalForcesModule::RemoveInfluenceAtIndex_Injected` | 引擎(libunity) | impl=0x17BB28 |
| `UnityEngine.ParticleSystem/ExternalForcesModule::SetInfluence_Injected` | 引擎(libunity) | impl=0x17BBD4 |
| `UnityEngine.ParticleSystem/ExternalForcesModule::get_influenceCount_Injected` | 引擎(libunity) | impl=0x17B98C |
| `UnityEngine.ParticleSystem/ExternalForcesModule::set_influenceFilter_Injected` | 引擎(libunity) | impl=0x17B8FC |
| `UnityEngine.ParticleSystem/MainModule::get_startColor_Injected` | 引擎(libunity) | impl=0x17AD24 |
| `UnityEngine.ParticleSystem/MainModule::set_simulationSpeed_Injected` | 引擎(libunity) | impl=0x17AEA0 |
| `UnityEngine.ParticleSystem/MainModule::set_startColor_Injected` | 引擎(libunity) | impl=0x17ADC8 |
| `UnityEngine.ParticleSystem/MainModule::set_startLifetimeMultiplier_Injected` | 引擎(libunity) | impl=0x17AB68 |
| `UnityEngine.ParticleSystem/MainModule::set_startSpeedMultiplier_Injected` | 引擎(libunity) | impl=0x17AC6C |
| `UnityEngine.ParticleSystem::EmitOld_Internal(UnityEngine.ParticleSystem/Particle&)` | 引擎+游戏 | impl=0x17A9C8 |
| `UnityEngine.ParticleSystem::Emit_Injected(UnityEngine.ParticleSystem/EmitParams&,System.Int32)` | 引擎+游戏 | impl=0x17A8C4 |
| `UnityEngine.ParticleSystem::Emit_Internal(System.Int32)` | 引擎+游戏 | impl=0x17A7D0 |
| `UnityEngine.ParticleSystem::Play(System.Boolean)` | 引擎+游戏 | impl=0x17A53C |
| `UnityEngine.ParticleSystem::Stop(System.Boolean,UnityEngine.ParticleSystemStopBehavior)` | 引擎+游戏 | impl=0x17A67C |
| `UnityEngine.ParticleSystemRenderer::GetMeshes(UnityEngine.Mesh[])` | 引擎+游戏 | impl=0x17BD4C |
| `UnityEngine.Physics2D::get_queriesHitTriggers()` | 引擎+游戏 | impl=0x5F9DC4 |
| `UnityEngine.Rigidbody2D::MovePosition_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x5FA248 |
| `UnityEngine.Rigidbody2D::MoveRotation_Angle(System.Single)` | 引擎+游戏 | impl=0x5FA334 |
| `UnityEngine.Rigidbody2D::get_position_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x5F9F84 |
| `UnityEngine.Rigidbody2D::get_rotation()` | 引擎+游戏 | impl=0x5FA164 |
| `UnityEngine.Rigidbody2D::set_position_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x5FA078 |
| `AnimationCurveBindings::GetKey` | 游戏(libil2cpp) | — |
| `AnimationCurveBindings::Internal_Create` | 游戏(libil2cpp) | — |
| `AnimationCurveBindings::Internal_Destroy` | 游戏(libil2cpp) | — |
| `AnimationCurveBindings::Internal_Equals` | 游戏(libil2cpp) | — |
| `AnimatorBindings::ResetTriggerString` | 游戏(libil2cpp) | — |
| `AnimatorBindings::SetTriggerString` | 游戏(libil2cpp) | — |
| `CUnityEngine.Animation::GetState(System.String)` | 游戏(libil2cpp) | — |
| `EmissionModule::get_burstCount_Injected(UnityEngine.ParticleSystem/EmissionModule&)` | 游戏(libil2cpp) | — |
| `EmissionModule::GetBurst_Injected(UnityEngine.ParticleSystem/EmissionModule&,System.Int32,UnityEngine.ParticleSystem/Burst&)` | 游戏(libil2cpp) | — |
| `EmissionModule::set_burstCount_Injected(UnityEngine.ParticleSystem/EmissionModule&,System.Int32)` | 游戏(libil2cpp) | — |
| `EmissionModule::set_rateOverDistanceMultiplier_Injected(UnityEngine.ParticleSystem/EmissionModule&,System.Single)` | 游戏(libil2cpp) | — |
| `EmissionModule::set_rateOverTimeMultiplier_Injected(UnityEngine.ParticleSystem/EmissionModule&,System.Single)` | 游戏(libil2cpp) | — |
| `EmissionModule::SetBurst_Injected(UnityEngine.ParticleSystem/EmissionModule&,System.Int32,UnityEngine.ParticleSystem/Burst&)` | 游戏(libil2cpp) | — |
| `ExternalForcesModule::AddInfluence_Injected(UnityEngine.ParticleSystem/ExternalForcesModule&,UnityEngine.ParticleSystemForceField)` | 游戏(libil2cpp) | — |
| `ExternalForcesModule::get_influenceCount_Injected(UnityEngine.ParticleSystem/ExternalForcesModule&)` | 游戏(libil2cpp) | — |
| `ExternalForcesModule::RemoveInfluenceAtIndex_Injected(UnityEngine.ParticleSystem/ExternalForcesModule&,System.Int32)` | 游戏(libil2cpp) | — |
| `ExternalForcesModule::SetInfluence_Injected(UnityEngine.ParticleSystem/ExternalForcesModule&,System.Int32,UnityEngine.ParticleSystemForceField)` | 游戏(libil2cpp) | — |
| `GraphicsScripting::GetMaxDrawMeshInstanceCount` | 游戏(libil2cpp) | — |
| `MainModule::get_startColor_Injected(UnityEngine.ParticleSystem/MainModule&,UnityEngine.ParticleSystem/MinMaxGradient&)` | 游戏(libil2cpp) | — |
| `MainModule::set_simulationSpeed_Injected(UnityEngine.ParticleSystem/MainModule&,System.Single)` | 游戏(libil2cpp) | — |
| `MainModule::set_startColor_Injected(UnityEngine.ParticleSystem/MainModule&,UnityEngine.ParticleSystem/MinMaxGradient&)` | 游戏(libil2cpp) | — |
| `MainModule::set_startLifetimeMultiplier_Injected(UnityEngine.ParticleSystem/MainModule&,System.Single)` | 游戏(libil2cpp) | — |
| `MainModule::set_startSpeedMultiplier_Injected(UnityEngine.ParticleSystem/MainModule&,System.Single)` | 游戏(libil2cpp) | — |
| `MeshScripting::CreateMesh` | 游戏(libil2cpp) | — |
| `MeshScripting::GetIndices` | 游戏(libil2cpp) | — |
| `MeshScripting::HasChannel` | 游戏(libil2cpp) | — |
| `MeshScripting::PrintErrorCantAccessChannel` | 游戏(libil2cpp) | — |
| `ParticleSystemRendererScriptBindings::GetMeshes` | 游戏(libil2cpp) | — |
| `ParticleSystemScriptBindings::Play` | 游戏(libil2cpp) | — |
| `ParticleSystemScriptBindings::Stop` | 游戏(libil2cpp) | — |

## GameObject / 组件 / 变换（74）

| ICall | 侧 | libunity impl |
|---|---|---|
| `UnityEngine.Behaviour::get_enabled()` | 引擎+游戏 | impl=0x39D1EC |
| `UnityEngine.Behaviour::get_isActiveAndEnabled()` | 引擎+游戏 | impl=0x39D3CC |
| `UnityEngine.Behaviour::set_enabled(System.Boolean)` | 引擎+游戏 | impl=0x39D2D8 |
| `UnityEngine.Component::GetComponentFastPath(System.Type,System.IntPtr)` | 引擎+游戏 | impl=0x39D754 |
| `UnityEngine.Component::GetComponentsForListInternal(System.Type,System.Object)` | 引擎+游戏 | impl=0x39D910 |
| `UnityEngine.Component::get_gameObject()` | 引擎+游戏 | impl=0x39D650 |
| `UnityEngine.Component::get_transform()` | 引擎+游戏 | impl=0x39D4B4 |
| `UnityEngine.MonoBehaviour::CancelInvoke(UnityEngine.MonoBehaviour,System.String)` | 引擎+游戏 | impl=0x39F504 |
| `UnityEngine.MonoBehaviour::GetScriptClassName()` | 引擎+游戏 | impl=0x39FCE8 |
| `UnityEngine.MonoBehaviour::Internal_CancelInvokeAll(UnityEngine.MonoBehaviour)` | 引擎+游戏 | impl=0x39F2B4 |
| `UnityEngine.MonoBehaviour::Internal_IsInvokingAll(UnityEngine.MonoBehaviour)` | 引擎+游戏 | impl=0x39F340 |
| `UnityEngine.MonoBehaviour::InvokeDelayed(UnityEngine.MonoBehaviour,System.String,System.Single,System.Single)` | 引擎+游戏 | impl=0x39F3D0 |
| `UnityEngine.MonoBehaviour::IsInvoking(UnityEngine.MonoBehaviour,System.String)` | 引擎+游戏 | impl=0x39F620 |
| `UnityEngine.MonoBehaviour::IsObjectMonoBehaviour(UnityEngine.Object)` | 引擎+游戏 | impl=0x39F744 |
| `UnityEngine.MonoBehaviour::StartCoroutineManaged(System.String,System.Object)` | 引擎+游戏 | impl=0x39F7D4 |
| `UnityEngine.MonoBehaviour::StartCoroutineManaged2(System.Collections.IEnumerator)` | 引擎+游戏 | impl=0x39F9A4 |
| `UnityEngine.MonoBehaviour::StopAllCoroutines()` | 引擎+游戏 | impl=0x39EFF8 |
| `UnityEngine.MonoBehaviour::StopCoroutine(System.String)` | 引擎+游戏 | impl=0x39EE74 |
| `UnityEngine.MonoBehaviour::StopCoroutineFromEnumeratorManaged(System.Collections.IEnumerator)` | 引擎+游戏 | impl=0x39FBD0 |
| `UnityEngine.MonoBehaviour::StopCoroutineManaged(UnityEngine.Coroutine)` | 引擎+游戏 | impl=0x39FABC |
| `UnityEngine.RectTransform::get_anchorMax_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x3A17C4 |
| `UnityEngine.RectTransform::get_anchorMin_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x3A15F0 |
| `UnityEngine.RectTransform::get_anchoredPosition_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x3A1998 |
| `UnityEngine.RectTransform::get_pivot_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x3A1D7C |
| `UnityEngine.RectTransform::get_rect_Injected(UnityEngine.Rect&)` | 引擎+游戏 | impl=0x3A1500 |
| `UnityEngine.RectTransform::get_sizeDelta_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x3A1BA8 |
| `UnityEngine.RectTransform::set_anchorMax_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x3A18AC |
| `UnityEngine.RectTransform::set_anchorMin_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x3A16D8 |
| `UnityEngine.RectTransform::set_anchoredPosition_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x3A1ABC |
| `UnityEngine.RectTransform::set_pivot_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x3A1E64 |
| `UnityEngine.RectTransform::set_sizeDelta_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x3A1C90 |
| `UnityEngine.RectTransformUtility::PixelAdjustPoint_Injected(UnityEngine.Vector2&,UnityEngine.Transform,UnityEngine.Canvas,UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x17F0DC |
| `UnityEngine.RectTransformUtility::PixelAdjustRect_Injected(UnityEngine.RectTransform,UnityEngine.Canvas,UnityEngine.Rect&)` | 引擎+游戏 | impl=0x17F210 |
| `UnityEngine.RectTransformUtility::PointInRectangle_Injected(UnityEngine.Vector2&,UnityEngine.RectTransform,UnityEngine.Camera,UnityEngine.Vector4&)` | 引擎+游戏 | impl=0x17F338 |
| `UnityEngine.Transform::GetChild(System.Int32)` | 引擎+游戏 | impl=0x3A3B38 |
| `UnityEngine.Transform::GetParent()` | 引擎+游戏 | impl=0x3A28C0 |
| `UnityEngine.Transform::GetRoot()` | 引擎+游戏 | impl=0x3A3264 |
| `UnityEngine.Transform::GetSiblingIndex()` | 引擎+游戏 | impl=0x3A36F4 |
| `UnityEngine.Transform::InverseTransformPoint_Injected(UnityEngine.Vector3&,UnityEngine.Vector3&)` | 引擎+游戏 | impl=0x3A3154 |
| `UnityEngine.Transform::InverseTransformVector_Injected(UnityEngine.Vector3&,UnityEngine.Vector3&)` | 引擎+游戏 | impl=0x3A2F34 |
| `UnityEngine.Transform::IsChildOf(UnityEngine.Transform)` | 引擎+游戏 | impl=0x3A38D4 |
| `UnityEngine.Transform::SetAsFirstSibling()` | 引擎+游戏 | impl=0x3A342C |
| `UnityEngine.Transform::SetAsLastSibling()` | 引擎+游戏 | impl=0x3A3518 |
| `UnityEngine.Transform::SetParent(UnityEngine.Transform,System.Boolean)` | 引擎+游戏 | impl=0x3A29A8 |
| `UnityEngine.Transform::SetSiblingIndex(System.Int32)` | 引擎+游戏 | impl=0x3A3604 |
| `UnityEngine.Transform::TransformDirection_Injected(UnityEngine.Vector3&,UnityEngine.Vector3&)` | 引擎+游戏 | impl=0x3A2D14 |
| `UnityEngine.Transform::TransformPoint_Injected(UnityEngine.Vector3&,UnityEngine.Vector3&)` | 引擎+游戏 | impl=0x3A3044 |
| `UnityEngine.Transform::TransformVector_Injected(UnityEngine.Vector3&,UnityEngine.Vector3&)` | 引擎+游戏 | impl=0x3A2E24 |
| `UnityEngine.Transform::get_childCount()` | 引擎+游戏 | impl=0x3A334C |
| `UnityEngine.Transform::get_localPosition_Injected(UnityEngine.Vector3&)` | 引擎+游戏 | impl=0x3A2138 |
| `UnityEngine.Transform::get_localRotation_Injected(UnityEngine.Quaternion&)` | 引擎+游戏 | impl=0x3A24FC |
| `UnityEngine.Transform::get_localScale_Injected(UnityEngine.Vector3&)` | 引擎+游戏 | impl=0x3A26D8 |
| `UnityEngine.Transform::get_localToWorldMatrix_Injected(UnityEngine.Matrix4x4&)` | 引擎+游戏 | impl=0x3A2C1C |
| `UnityEngine.Transform::get_lossyScale_Injected(UnityEngine.Vector3&)` | 引擎+游戏 | impl=0x3A37D8 |
| `UnityEngine.Transform::get_position_Injected(UnityEngine.Vector3&)` | 引擎+游戏 | impl=0x3A1F50 |
| `UnityEngine.Transform::get_rotation_Injected(UnityEngine.Quaternion&)` | 引擎+游戏 | impl=0x3A2320 |
| `UnityEngine.Transform::get_worldToLocalMatrix_Injected(UnityEngine.Matrix4x4&)` | 引擎+游戏 | impl=0x3A2B24 |
| `UnityEngine.Transform::set_hasChanged(System.Boolean)` | 引擎+游戏 | impl=0x3A3A48 |
| `UnityEngine.Transform::set_localPosition_Injected(UnityEngine.Vector3&)` | 引擎+游戏 | impl=0x3A2234 |
| `UnityEngine.Transform::set_localRotation_Injected(UnityEngine.Quaternion&)` | 引擎+游戏 | impl=0x3A25EC |
| `UnityEngine.Transform::set_localScale_Injected(UnityEngine.Vector3&)` | 引擎+游戏 | impl=0x3A27D4 |
| `UnityEngine.Transform::set_position_Injected(UnityEngine.Vector3&)` | 引擎+游戏 | impl=0x3A204C |
| `UnityEngine.Transform::set_rotation_Injected(UnityEngine.Quaternion&)` | 引擎+游戏 | impl=0x3A2410 |
| `ExternalForcesModule::set_influenceFilter_Injected(UnityEngine.ParticleSystem/ExternalForcesModule&,UnityEngine.ParticleSystemGameObjectFilter)` | 游戏(libil2cpp) | — |
| `GameObjectBindings::GetComponentFastPath` | 游戏(libil2cpp) | — |
| `GameObjectBindings::GetComponentFromType` | 游戏(libil2cpp) | — |
| `GameObjectBindings::GetComponentInChildren` | 游戏(libil2cpp) | — |
| `GameObjectBindings::GetComponentInParent` | 游戏(libil2cpp) | — |
| `GameObjectBindings::GetComponentsInternal` | 游戏(libil2cpp) | — |
| `GameObjectBindings::GetTransform` | 游戏(libil2cpp) | — |
| `GameObjectBindings::Internal_CreateGameObject` | 游戏(libil2cpp) | — |
| `GameObjectBindings::TryGetComponentFastPath` | 游戏(libil2cpp) | — |
| `UI::RectTransform` | 游戏(libil2cpp) | — |
| `Unity::Component` | 游戏(libil2cpp) | — |

## 场景 / 输入 / 时间（64）

| ICall | 侧 | libunity impl |
|---|---|---|
| `UnityEngine.Application::OpenURL(System.String)` | 引擎+游戏 | impl=0x392640 |
| `UnityEngine.Application::Quit(System.Int32)` | 引擎+游戏 | impl=0x392484 |
| `UnityEngine.Application::get_dataPath()` | 引擎+游戏 | impl=0x3924CC |
| `UnityEngine.Application::get_isFocused()` | 引擎+游戏 | impl=0x3924B8 |
| `UnityEngine.Application::get_isPlaying()` | 引擎+游戏 | impl=0x3924A4 |
| `UnityEngine.Application::get_persistentDataPath()` | 引擎+游戏 | impl=0x3925AC |
| `UnityEngine.Application::get_platform()` | 引擎+游戏 | impl=0x3926F0 |
| `UnityEngine.Application::get_productName()` | 引擎+游戏 | impl=0x39261C |
| `UnityEngine.Application::get_streamingAssetsPath()` | 引擎+游戏 | impl=0x39253C |
| `UnityEngine.Application::get_systemLanguage()` | 引擎+游戏 | impl=0x3926F4 |
| `UnityEngine.Application::set_targetFrameRate(System.Int32)` | 引擎+游戏 | impl=0x3926EC |
| `UnityEngine.Input::GetAxis(System.String)` | 引擎+游戏 | impl=0x1794DC |
| `UnityEngine.Input::GetAxisRaw(System.String)` | 引擎+游戏 | impl=0x179620 |
| `UnityEngine.Input::GetButtonDown(System.String)` | 引擎+游戏 | impl=0x179764 |
| `UnityEngine.Input::GetKeyDownInt(UnityEngine.KeyCode)` | 引擎+游戏 | impl=0x17926C |
| `UnityEngine.Input::GetKeyDownString(System.String)` | 引擎+游戏 | impl=0x179354 |
| `UnityEngine.Input::GetKeyInt(UnityEngine.KeyCode)` | 引擎+游戏 | impl=0x179178 |
| `UnityEngine.Input::GetMouseButton(System.Int32)` | 引擎+游戏 | impl=0x1798A8 |
| `UnityEngine.Input::GetMouseButtonDown(System.Int32)` | 引擎+游戏 | impl=0x179990 |
| `UnityEngine.Input::GetMouseButtonUp(System.Int32)` | 引擎+游戏 | impl=0x179A6C |
| `UnityEngine.Input::GetTouch_Injected(System.Int32,UnityEngine.Touch&)` | 引擎+游戏 | impl=0x179B48 |
| `UnityEngine.Input::get_anyKeyDown()` | 引擎+游戏 | impl=0x179C0C |
| `UnityEngine.Input::get_compositionCursorPos_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x179EE0 |
| `UnityEngine.Input::get_compositionString()` | 引擎+游戏 | impl=0x179E54 |
| `UnityEngine.Input::get_imeCompositionMode()` | 引擎+游戏 | impl=0x179E28 |
| `UnityEngine.Input::get_mousePosition_Injected(UnityEngine.Vector3&)` | 引擎+游戏 | impl=0x179CAC |
| `UnityEngine.Input::get_mousePresent()` | 引擎+游戏 | impl=0x179F18 |
| `UnityEngine.Input::get_mouseScrollDelta_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x179D78 |
| `UnityEngine.Input::get_touchCount()` | 引擎+游戏 | impl=0x179F2C |
| `UnityEngine.Input::get_touchSupported()` | 引擎+游戏 | impl=0x179F3C |
| `UnityEngine.Input::set_compositionCursorPos_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x179EFC |
| `UnityEngine.Input::set_imeCompositionMode(UnityEngine.IMECompositionMode)` | 引擎+游戏 | impl=0x179E3C |
| `UnityEngine.Physics2D::GetRayIntersectionAll_Internal_Injected(UnityEngine.PhysicsScene2D&,UnityEngine.Vector3&,UnityEngine.Vector3&,System.Single,System.Int32)` | 引擎+游戏 | impl=0x5F9DD8 |
| `UnityEngine.Physics::Internal_RaycastAll_Injected(UnityEngine.PhysicsScene&,UnityEngine.Ray&,System.Single,System.Int32,UnityEngine.QueryTriggerInteraction)` | 引擎+游戏 | impl=0x5E5A60 |
| `UnityEngine.Physics::get_defaultPhysicsScene_Injected(UnityEngine.PhysicsScene&)` | 引擎+游戏 | impl=0x5E5A3C |
| `UnityEngine.PhysicsScene2D::GetRayIntersectionArray_Internal_Injected(UnityEngine.PhysicsScene2D&,UnityEngine.Vector3&,UnityEngine.Vector3&,System.Single,System.Int32,UnityEngine.RaycastHit2D[])` | 引擎+游戏 | impl=0x5F9CE8 |
| `UnityEngine.PhysicsScene2D::RaycastArray_Internal_Injected(UnityEngine.PhysicsScene2D&,UnityEngine.Vector2&,UnityEngine.Vector2&,System.Single,UnityEngine.ContactFilter2D&,UnityEngine.RaycastHit2D[])` | 引擎+游戏 | impl=0x5F9B44 |
| `UnityEngine.PhysicsScene2D::RaycastList_Internal_Injected(UnityEngine.PhysicsScene2D&,UnityEngine.Vector2&,UnityEngine.Vector2&,System.Single,UnityEngine.ContactFilter2D&,System.Collections.Generic.List`1<UnityEngine.RaycastHit2D>)` | 引擎+游戏 | impl=0x5F9C20 |
| `UnityEngine.PhysicsScene2D::Raycast_Internal_Injected(UnityEngine.PhysicsScene2D&,UnityEngine.Vector2&,UnityEngine.Vector2&,System.Single,UnityEngine.ContactFilter2D&,UnityEngine.RaycastHit2D&)` | 引擎+游戏 | impl=0x5F9B0C |
| `UnityEngine.PhysicsScene::Internal_RaycastNonAlloc_Injected(UnityEngine.PhysicsScene&,UnityEngine.Ray&,UnityEngine.RaycastHit[],System.Single,System.Int32,UnityEngine.QueryTriggerInteraction)` | 引擎+游戏 | impl=0x5E5998 |
| `UnityEngine.PhysicsScene::Internal_RaycastTest_Injected(UnityEngine.PhysicsScene&,UnityEngine.Ray&,System.Single,System.Int32,UnityEngine.QueryTriggerInteraction)` | 引擎+游戏 | impl=0x5E58D8 |
| `UnityEngine.PhysicsScene::Internal_Raycast_Injected(UnityEngine.PhysicsScene&,UnityEngine.Ray&,System.Single,UnityEngine.RaycastHit&,System.Int32,UnityEngine.QueryTriggerInteraction)` | 引擎+游戏 | impl=0x5E5934 |
| `UnityEngine.SceneManagement.SceneManager::GetSceneAt_Injected(System.Int32,UnityEngine.SceneManagement.Scene&)` | 引擎+游戏 | impl=0x3A518C |
| `UnityEngine.SceneManagement.SceneManager::get_sceneCount()` | 引擎+游戏 | impl=0x3A5178 |
| `UnityEngine.SceneManagement.SceneManagerAPIInternal::LoadSceneAsyncNameIndexInternal_Injected(System.String,System.Int32,UnityEngine.SceneManagement.LoadSceneParameters&,System.Boolean)` | 引擎+游戏 | impl=0x3A5080 |
| `UnityEngine.Screen::SetResolution(System.Int32,System.Int32,UnityEngine.FullScreenMode,System.Int32)` | 引擎+游戏 | impl=0x395438 |
| `UnityEngine.Screen::get_currentResolution_Injected(UnityEngine.Resolution&)` | 引擎+游戏 | impl=0x3953BC |
| `UnityEngine.Screen::get_dpi()` | 引擎+游戏 | impl=0x3953A4 |
| `UnityEngine.Screen::get_fullScreen()` | 引擎+游戏 | impl=0x3953E4 |
| `UnityEngine.Screen::get_fullScreenMode()` | 引擎+游戏 | impl=0x395420 |
| `UnityEngine.Screen::get_height()` | 引擎+游戏 | impl=0x39538C |
| `UnityEngine.Screen::get_resolutions()` | 引擎+游戏 | impl=0x395480 |
| `UnityEngine.Screen::get_width()` | 引擎+游戏 | impl=0x395374 |
| `UnityEngine.Screen::set_fullScreen(System.Boolean)` | 引擎+游戏 | impl=0x395404 |
| `UnityEngine.SystemInfo::GetOperatingSystemFamily()` | 引擎+游戏 | impl=0x3A0D2C |
| `UnityEngine.Time::get_deltaTime()` | 引擎+游戏 | impl=0x3A0D60 |
| `UnityEngine.Time::get_fixedDeltaTime()` | 引擎+游戏 | impl=0x3A0DA0 |
| `UnityEngine.Time::get_frameCount()` | 引擎+游戏 | impl=0x3A0DDC |
| `UnityEngine.Time::get_realtimeSinceStartup()` | 引擎+游戏 | impl=0x3A0DF0 |
| `UnityEngine.Time::get_smoothDeltaTime()` | 引擎+游戏 | impl=0x3A0DB4 |
| `UnityEngine.Time::get_timeScale()` | 引擎+游戏 | impl=0x3A0DC8 |
| `UnityEngine.Time::get_unscaledDeltaTime()` | 引擎+游戏 | impl=0x3A0D8C |
| `UnityEngine.Time::get_unscaledTime()` | 引擎+游戏 | impl=0x3A0D74 |
| `UnityEngine.TouchScreenKeyboard::set_hideInput(System.Boolean)` | 引擎+游戏 | impl=0x3A11D4 |

## UI / Canvas（54）

| ICall | 侧 | libunity impl |
|---|---|---|
| `UnityEngine.Canvas::GetDefaultCanvasMaterial()` | 引擎+游戏 | impl=0x180804 |
| `UnityEngine.Canvas::GetETC1SupportedCanvasMaterial()` | 引擎+游戏 | impl=0x18081C |
| `UnityEngine.Canvas::get_additionalShaderChannels()` | 引擎+游戏 | impl=0x180324 |
| `UnityEngine.Canvas::get_isRootCanvas()` | 引擎+游戏 | impl=0x17F54C |
| `UnityEngine.Canvas::get_overrideSorting()` | 引擎+游戏 | impl=0x17FCBC |
| `UnityEngine.Canvas::get_pixelPerfect()` | 引擎+游戏 | impl=0x17F9FC |
| `UnityEngine.Canvas::get_referencePixelsPerUnit()` | 引擎+游戏 | impl=0x17F818 |
| `UnityEngine.Canvas::get_renderMode()` | 引擎+游戏 | impl=0x17F468 |
| `UnityEngine.Canvas::get_renderOrder()` | 引擎+游戏 | impl=0x17FBD8 |
| `UnityEngine.Canvas::get_renderingDisplaySize_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x1805F0 |
| `UnityEngine.Canvas::get_rootCanvas()` | 引擎+游戏 | impl=0x1804F0 |
| `UnityEngine.Canvas::get_scaleFactor()` | 引擎+游戏 | impl=0x17F634 |
| `UnityEngine.Canvas::get_sortingLayerID()` | 引擎+游戏 | impl=0x180154 |
| `UnityEngine.Canvas::get_sortingOrder()` | 引擎+游戏 | impl=0x17FE8C |
| `UnityEngine.Canvas::get_targetDisplay()` | 引擎+游戏 | impl=0x180070 |
| `UnityEngine.Canvas::get_worldCamera()` | 引擎+游戏 | impl=0x1806E4 |
| `UnityEngine.Canvas::set_additionalShaderChannels(UnityEngine.AdditionalCanvasShaderChannels)` | 引擎+游戏 | impl=0x180404 |
| `UnityEngine.Canvas::set_overrideSorting(System.Boolean)` | 引擎+游戏 | impl=0x17FD9C |
| `UnityEngine.Canvas::set_planeDistance(System.Single)` | 引擎+游戏 | impl=0x17FAE4 |
| `UnityEngine.Canvas::set_referencePixelsPerUnit(System.Single)` | 引擎+游戏 | impl=0x17F908 |
| `UnityEngine.Canvas::set_scaleFactor(System.Single)` | 引擎+游戏 | impl=0x17F724 |
| `UnityEngine.Canvas::set_sortingLayerID(System.Int32)` | 引擎+游戏 | impl=0x180238 |
| `UnityEngine.Canvas::set_sortingOrder(System.Int32)` | 引擎+游戏 | impl=0x17FF84 |
| `UnityEngine.CanvasGroup::get_alpha()` | 引擎+游戏 | impl=0x17CF28 |
| `UnityEngine.CanvasGroup::get_blocksRaycasts()` | 引擎+游戏 | impl=0x17D2CC |
| `UnityEngine.CanvasGroup::get_ignoreParentGroups()` | 引擎+游戏 | impl=0x17D49C |
| `UnityEngine.CanvasGroup::get_interactable()` | 引擎+游戏 | impl=0x17D0FC |
| `UnityEngine.CanvasGroup::set_alpha(System.Single)` | 引擎+游戏 | impl=0x17D008 |
| `UnityEngine.CanvasGroup::set_blocksRaycasts(System.Boolean)` | 引擎+游戏 | impl=0x17D3AC |
| `UnityEngine.CanvasGroup::set_interactable(System.Boolean)` | 引擎+游戏 | impl=0x17D1DC |
| `UnityEngine.CanvasRenderer::Clear()` | 引擎+游戏 | impl=0x17EB5C |
| `UnityEngine.CanvasRenderer::CreateUIVertexStreamInternal(System.Object,System.Object,System.Object,System.Object,System.Object,System.Object,System.Object,System.Object,System.Object,System.Object)` | 引擎+游戏 | impl=0x17EEB8 |
| `UnityEngine.CanvasRenderer::DisableRectClipping()` | 引擎+游戏 | impl=0x17E25C |
| `UnityEngine.CanvasRenderer::EnableRectClipping_Injected(UnityEngine.Rect&)` | 引擎+游戏 | impl=0x17E078 |
| `UnityEngine.CanvasRenderer::GetColor_Injected(UnityEngine.Color&)` | 引擎+游戏 | impl=0x17DF88 |
| `UnityEngine.CanvasRenderer::GetMaterial(System.Int32)` | 引擎+游戏 | impl=0x17E4B8 |
| `UnityEngine.CanvasRenderer::SetColor_Injected(UnityEngine.Color&)` | 引擎+游戏 | impl=0x17DE9C |
| `UnityEngine.CanvasRenderer::SetMaterial(UnityEngine.Material,System.Int32)` | 引擎+游戏 | impl=0x17E340 |
| `UnityEngine.CanvasRenderer::SetPopMaterial(UnityEngine.Material,System.Int32)` | 引擎+游戏 | impl=0x17E5AC |
| `UnityEngine.CanvasRenderer::SplitIndicesStreamsInternal(System.Object,System.Object)` | 引擎+游戏 | impl=0x17EC40 |
| `UnityEngine.CanvasRenderer::SplitUIVertexStreamsInternal(System.Object,System.Object,System.Object,System.Object,System.Object,System.Object,System.Object,System.Object,System.Object)` | 引擎+游戏 | impl=0x17ECC4 |
| `UnityEngine.CanvasRenderer::get_absoluteDepth()` | 引擎+游戏 | impl=0x17D924 |
| `UnityEngine.CanvasRenderer::get_cull()` | 引擎+游戏 | impl=0x17DCCC |
| `UnityEngine.CanvasRenderer::get_hasMoved()` | 引擎+游戏 | impl=0x17DA14 |
| `UnityEngine.CanvasRenderer::get_materialCount()` | 引擎+游戏 | impl=0x17D66C |
| `UnityEngine.CanvasRenderer::set_clippingSoftness_Injected(UnityEngine.Vector2&)` | 引擎+游戏 | impl=0x17E168 |
| `UnityEngine.CanvasRenderer::set_cull(System.Boolean)` | 引擎+游戏 | impl=0x17DDAC |
| `UnityEngine.CanvasRenderer::set_hasPopInstruction(System.Boolean)` | 引擎+游戏 | impl=0x17D57C |
| `UnityEngine.CanvasRenderer::set_materialCount(System.Int32)` | 引擎+游戏 | impl=0x17D74C |
| `UnityEngine.CanvasRenderer::set_popMaterialCount(System.Int32)` | 引擎+游戏 | impl=0x17D838 |
| `UI::Canvas` | 游戏(libil2cpp) | — |
| `UI::CanvasGroup` | 游戏(libil2cpp) | — |
| `UI::CanvasRenderer` | 游戏(libil2cpp) | — |
| `UI::GetETC1SupportedCanvasMaterial` | 游戏(libil2cpp) | — |

## 资源 / 资产 / 序列化（43）

| ICall | 侧 | libunity impl |
|---|---|---|
| `UnityEngine.GameObject::GetComponent(System.Type)` | 引擎+游戏 | impl=0x39DAB8 |
| `UnityEngine.GameObject::GetComponentFastPath(System.Type,System.IntPtr)` | 引擎+游戏 | impl=0x39DC54 |
| `UnityEngine.GameObject::GetComponentInChildren(System.Type,System.Boolean)` | 引擎+游戏 | impl=0x39DDDC |
| `UnityEngine.GameObject::GetComponentInParent(System.Type)` | 引擎+游戏 | impl=0x39DF98 |
| `UnityEngine.GameObject::GetComponentsInternal(System.Type,System.Boolean,System.Boolean,System.Boolean,System.Boolean,System.Object)` | 引擎+游戏 | impl=0x39E140 |
| `UnityEngine.GameObject::Internal_AddComponentWithType(System.Type)` | 引擎+游戏 | impl=0x39E49C |
| `UnityEngine.GameObject::Internal_CreateGameObject(UnityEngine.GameObject,System.String)` | 引擎+游戏 | impl=0x39ED58 |
| `UnityEngine.GameObject::SendMessage(System.String,System.Object,UnityEngine.SendMessageOptions)` | 引擎+游戏 | impl=0x39EB90 |
| `UnityEngine.GameObject::SetActive(System.Boolean)` | 引擎+游戏 | impl=0x39E8D8 |
| `UnityEngine.GameObject::TryGetComponentFastPath(System.Type,System.IntPtr)` | 引擎+游戏 | impl=0x39E314 |
| `UnityEngine.GameObject::get_activeInHierarchy()` | 引擎+游戏 | impl=0x39EAA8 |
| `UnityEngine.GameObject::get_activeSelf()` | 引擎+游戏 | impl=0x39E9C8 |
| `UnityEngine.GameObject::get_layer()` | 引擎+游戏 | impl=0x39E70C |
| `UnityEngine.GameObject::get_transform()` | 引擎+游戏 | impl=0x39E5D0 |
| `UnityEngine.GameObject::set_layer(System.Int32)` | 引擎+游戏 | impl=0x39E7EC |
| `UnityEngine.JsonUtility::FromJsonInternal(System.String,System.Object,System.Type)` | 引擎+游戏 | impl=0x17A3C0 |
| `UnityEngine.JsonUtility::ToJsonInternal(System.Object,System.Boolean)` | 引擎+游戏 | impl=0x17A2CC |
| `UnityEngine.Object::Destroy(UnityEngine.Object,System.Single)` | 引擎+游戏 | impl=0x3A007C |
| `UnityEngine.Object::DestroyImmediate(UnityEngine.Object,System.Boolean)` | 引擎+游戏 | impl=0x3A0130 |
| `UnityEngine.Object::DontDestroyOnLoad(UnityEngine.Object)` | 引擎+游戏 | impl=0x3A0234 |
| `UnityEngine.Object::FindObjectFromInstanceID(System.Int32)` | 引擎+游戏 | impl=0x3A0B54 |
| `UnityEngine.Object::FindObjectsOfType(System.Type)` | 引擎+游戏 | impl=0x3A01E0 |
| `UnityEngine.Object::GetName(UnityEngine.Object)` | 引擎+游戏 | impl=0x3A08FC |
| `UnityEngine.Object::GetOffsetOfInstanceIDInCPlusPlusObject()` | 引擎+游戏 | impl=0x3A04A4 |
| `UnityEngine.Object::Internal_CloneSingle(UnityEngine.Object)` | 引擎+游戏 | impl=0x3A04A8 |
| `UnityEngine.Object::Internal_CloneSingleWithParent(UnityEngine.Object,UnityEngine.Transform,System.Boolean)` | 引擎+游戏 | impl=0x3A053C |
| `UnityEngine.Object::Internal_InstantiateSingleWithParent_Injected(UnityEngine.Object,UnityEngine.Transform,UnityEngine.Vector3&,UnityEngine.Quaternion&)` | 引擎+游戏 | impl=0x3A0700 |
| `UnityEngine.Object::Internal_InstantiateSingle_Injected(UnityEngine.Object,UnityEngine.Vector3&,UnityEngine.Quaternion&)` | 引擎+游戏 | impl=0x3A0654 |
| `UnityEngine.Object::SetName(UnityEngine.Object,System.String)` | 引擎+游戏 | impl=0x3A0A10 |
| `UnityEngine.Object::ToString(UnityEngine.Object)` | 引擎+游戏 | impl=0x3A081C |
| `UnityEngine.Object::get_hideFlags()` | 引擎+游戏 | impl=0x3A02D0 |
| `UnityEngine.Object::set_hideFlags(UnityEngine.HideFlags)` | 引擎+游戏 | impl=0x3A03B4 |
| `UnityEngine.Resources::GetBuiltinResource(System.Type,System.String)` | 引擎+游戏 | impl=0x39CF6C |
| `UnityEngine.Resources::Load(System.String,System.Type)` | 引擎+游戏 | impl=0x39CE10 |
| `UnityEngine.ScriptableObject::CreateScriptableObject(UnityEngine.ScriptableObject)` | 引擎+游戏 | impl=0x39FE7C |
| `UnityEngine.ScriptableObject::CreateScriptableObjectInstanceFromType(System.Type,System.Boolean)` | 引擎+游戏 | impl=0x39FF04 |
| `UnityEngine.TextAsset::get_text()` | 引擎+游戏 | impl=0x39FF88 |
| `__ComObject::CreateRCW` | 游戏(libil2cpp) | — |
| `__ComObject::GetInterfaceInternal` | 游戏(libil2cpp) | — |
| `__ComObject::ReleaseInterfaces` | 游戏(libil2cpp) | — |
| `Object::GetOffsetOfInstanceIdMember` | 游戏(libil2cpp) | — |
| `Resources_Bindings::Load` | 游戏(libil2cpp) | — |
| `Scripting::CreateScriptableObjectWithType` | 游戏(libil2cpp) | — |

## 网络 / 下载 / WebRequest（24）

| ICall | 侧 | libunity impl |
|---|---|---|
| `UnityEngine.Networking.CertificateHandler::Release()` | 引擎+游戏 | impl=0x614698 |
| `UnityEngine.Networking.DownloadHandler::GetContentType()` | 引擎+游戏 | impl=0x614790 |
| `UnityEngine.Networking.DownloadHandler::InternalGetByteArray(UnityEngine.Networking.DownloadHandler)` | 引擎+游戏 | impl=0x614820 |
| `UnityEngine.Networking.DownloadHandler::Release()` | 引擎+游戏 | impl=0x614714 |
| `UnityEngine.Networking.DownloadHandlerBuffer::Create(UnityEngine.Networking.DownloadHandlerBuffer)` | 引擎+游戏 | impl=0x614884 |
| `UnityEngine.Networking.UnityWebRequest::Abort()` | 引擎+游戏 | impl=0x613D7C |
| `UnityEngine.Networking.UnityWebRequest::BeginWebRequest()` | 引擎+游戏 | impl=0x613CE8 |
| `UnityEngine.Networking.UnityWebRequest::Create()` | 引擎+游戏 | impl=0x613C68 |
| `UnityEngine.Networking.UnityWebRequest::GetError()` | 引擎+游戏 | impl=0x614014 |
| `UnityEngine.Networking.UnityWebRequest::GetHTTPStatusString(System.Int64)` | 引擎+游戏 | impl=0x613C50 |
| `UnityEngine.Networking.UnityWebRequest::GetUrl()` | 引擎+游戏 | impl=0x614094 |
| `UnityEngine.Networking.UnityWebRequest::GetWebErrorString(UnityEngine.Networking.UnityWebRequest/UnityWebRequestError)` | 引擎+游戏 | impl=0x613C38 |
| `UnityEngine.Networking.UnityWebRequest::Release()` | 引擎+游戏 | impl=0x613C6C |
| `UnityEngine.Networking.UnityWebRequest::SetCustomMethod(System.String)` | 引擎+游戏 | impl=0x613EBC |
| `UnityEngine.Networking.UnityWebRequest::SetDownloadHandler(UnityEngine.Networking.DownloadHandler)` | 引擎+游戏 | impl=0x6145DC |
| `UnityEngine.Networking.UnityWebRequest::SetMethod(UnityEngine.Networking.UnityWebRequest/UnityWebRequestMethod)` | 引擎+游戏 | impl=0x613E00 |
| `UnityEngine.Networking.UnityWebRequest::SetUploadHandler(UnityEngine.Networking.UploadHandler)` | 引擎+游戏 | impl=0x614520 |
| `UnityEngine.Networking.UnityWebRequest::SetUrl(System.String)` | 引擎+游戏 | impl=0x614124 |
| `UnityEngine.Networking.UnityWebRequest::get_isDone()` | 引擎+游戏 | impl=0x614390 |
| `UnityEngine.Networking.UnityWebRequest::get_isHttpError()` | 引擎+游戏 | impl=0x61449C |
| `UnityEngine.Networking.UnityWebRequest::get_isModifiable()` | 引擎+游戏 | impl=0x61430C |
| `UnityEngine.Networking.UnityWebRequest::get_isNetworkError()` | 引擎+游戏 | impl=0x614418 |
| `UnityEngine.Networking.UnityWebRequest::get_responseCode()` | 引擎+游戏 | impl=0x614274 |
| `UnityEngine.Networking.UploadHandler::Release()` | 引擎+游戏 | impl=0x6148D4 |

## 音频 / 声音 / 麦克风（17）

| ICall | 侧 | libunity impl |
|---|---|---|
| `UnityEngine.Audio.AudioMixer::GetFloat(System.String,System.Single&)` | 引擎+游戏 | impl=0x1AB838 |
| `UnityEngine.Audio.AudioMixer::SetFloat(System.String,System.Single)` | 引擎+游戏 | impl=0x1AB68C |
| `UnityEngine.AudioClip::get_length()` | 引擎+游戏 | impl=0x1AA944 |
| `UnityEngine.AudioSettings::StartAudioOutput()` | 引擎+游戏 | impl=0x1AA914 |
| `UnityEngine.AudioSettings::StopAudioOutput()` | 引擎+游戏 | impl=0x1AA92C |
| `UnityEngine.AudioSource::GetPitch(UnityEngine.AudioSource)` | 引擎+游戏 | impl=0x1AAA28 |
| `UnityEngine.AudioSource::Play(System.Double)` | 引擎+游戏 | impl=0x1AADC0 |
| `UnityEngine.AudioSource::PlayHelper(UnityEngine.AudioSource,System.UInt64)` | 引擎+游戏 | impl=0x1AAC78 |
| `UnityEngine.AudioSource::SetPitch(UnityEngine.AudioSource,System.Single)` | 引擎+游戏 | impl=0x1AAAF8 |
| `UnityEngine.AudioSource::Stop(System.Boolean)` | 引擎+游戏 | impl=0x1AAEB4 |
| `UnityEngine.AudioSource::get_clip()` | 引擎+游戏 | impl=0x1AB17C |
| `UnityEngine.AudioSource::get_isPlaying()` | 引擎+游戏 | impl=0x1AB3D4 |
| `UnityEngine.AudioSource::get_playOnAwake()` | 引擎+游戏 | impl=0x1AB4BC |
| `UnityEngine.AudioSource::get_volume()` | 引擎+游戏 | impl=0x1AAFA4 |
| `UnityEngine.AudioSource::set_clip(UnityEngine.AudioClip)` | 引擎+游戏 | impl=0x1AB26C |
| `UnityEngine.AudioSource::set_playOnAwake(System.Boolean)` | 引擎+游戏 | impl=0x1AB59C |
| `UnityEngine.AudioSource::set_volume(System.Single)` | 引擎+游戏 | impl=0x1AB088 |

## UnityEngineObjectBindings（5）

| ICall | 侧 | libunity impl |
|---|---|---|
| `UnityEngineObjectBindings::FindObjectFromInstanceID` | 游戏(libil2cpp) | — |
| `UnityEngineObjectBindings::FindObjectsOfType` | 游戏(libil2cpp) | — |
| `UnityEngineObjectBindings::GetName` | 游戏(libil2cpp) | — |
| `UnityEngineObjectBindings::SetName` | 游戏(libil2cpp) | — |
| `UnityEngineObjectBindings::ToString` | 游戏(libil2cpp) | — |

## Unity（3）

| ICall | 侧 | libunity impl |
|---|---|---|
| `Unity.Collections.LowLevel.Unsafe.UnsafeUtility::Free(System.Void*,Unity.Collections.Allocator)` | 引擎+游戏 | impl=0x3920B4 |
| `Unity.Jobs.JobHandle::ScheduleBatchedJobs()` | 引擎+游戏 | impl=0x3920A4 |
| `Unity.Profiling.ProfilerMarker::Internal_Create(System.String,System.UInt16)` | 引擎+游戏 | impl=0x39209C |
