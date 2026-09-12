# Unity Plugin for LayaAir3.0

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
