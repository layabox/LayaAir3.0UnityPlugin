# Unity Plugin for LayaAir3.0

## 纹理透明度导入设置

纹理导出会将 Unity `TextureImporter.alphaIsTransparency` 写入图片
`.meta` 的 `importer.alphaIsTransparency`，保留源资源的开关值。没有
`TextureImporter` 的内置纹理写入 `false`。

图片仍通过 Unity 已导入的纹理像素生成，可能已包含 Unity 的扩边结果。
LayaPro 开启此选项时，在缩放、mipmap 和压缩之前重新计算完全透明像素的
RGB，扩边步骤不会修改 Alpha 或非完全透明像素的 RGB。相同输入重复执行
LayaPro 的扩边不会继续扩大范围；但 Unity 的缩放或压缩可能改变输入，
因此不保证两端透明区域的 RGB 逐像素一致。在 LayaPro 中关闭此选项只会
跳过其扩边步骤，无法还原已写入导出图片的 Unity 扩边结果。

当前 LayaPro 仅对普通 2D、可解码的 LDR 纹理且保留 Alpha 的输出执行此处理；
Sprite 的导出 meta 同样保留源配置，但目前不参与该步骤。该属性属于图片
导入设置，不写入材质 `propertyParams`，也不替代预乘 Alpha 设置。

## 材质映射中的 Cubemap

在材质映射条目的 `textures` 中配置 Cubemap，与 Texture2D 使用相同格式：

```json
"textures": [
  {
    "uName": "_Cube",
    "layaName": "u_CubeTexture"
  }
]
```

将示例属性名替换为实际的 Unity 纹理属性和 Laya Shader uniform 名称；Laya
Shader 对应 uniform 的类型应为 `TextureCube`。无需添加 `type` 字段，也无需
启用自定义 Shader 自动导出。可按原有格式配置 `define`（项目映射）或
`defind`（内置映射）的宏定义。

导出时根据材质绑定的资源类型识别 Cubemap，生成六张 PNG 和一个 `.cubemap`，
并在 `.lmat` 中写入资源 UUID 引用。同一 Cubemap 被多个材质引用时只导出一份。
六面像素按 Unity 到 Laya 的 X 轴转换约定调整方向；`.cubemap` 和材质引用中的
mipmap 开关统一跟随源 Cubemap 是否包含多级纹理。资源需经过 LayaAir IDE 导入，
由 IDE 将六张图片转换为运行时使用的 KTX。
沿用现有 Cubemap 导出格式：8 位 RGBA PNG，不保留 HDR 范围；无资源路径的
临时纹理、Unity 内置 Cubemap 和立方体 RenderTexture 不在此支持范围内。

## License

MIT
