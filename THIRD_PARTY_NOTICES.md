# 第三方组件与许可范围

本仓库公开的是“代理小助手”客户端源码。根目录 `LICENSE` 的非商业条款仅适用于 @GFVNIP 有权授权的原创客户端材料，不适用于下列第三方组件及许可文本。

## sing-box

| 项目 | 内容 |
| --- | --- |
| 上游 | [SagerNet/sing-box](https://github.com/SagerNet/sing-box) |
| 配套版本 | 1.14.3，Windows amd64 |
| 原始版权 | Copyright (C) 2022 by nekohasekai |
| 许可证 | GNU GPL 第 3 版或任何后续版本（GPL-3.0-or-later），另保留上游名称/关联声明 |
| 本仓库是否包含其代码或二进制 | 不包含；运行时通过独立 `core/sing-box.exe` 进程调用 |
| 上游发布 | [v1.14.3](https://github.com/SagerNet/sing-box/releases/tag/v1.14.3) |
| 上游源码 | [v1.14.3 源码树](https://github.com/SagerNet/sing-box/tree/v1.14.3) |
| 原始许可副本 | [sing-box-LICENSE.txt](LICENSES/sing-box-LICENSE.txt) |
| GPL 全文 | [GPL-3.0.txt](LICENSES/GPL-3.0.txt) |

原始许可副本从配套 v1.14.3 源码归档的顶层 `LICENSE` 原样保留，包含上游关于衍生作品名称及关联表述的附加声明。此项目使用“代理小助手”名称，不声称是 sing-box 官方产品或获得其作者背书。

GPL 对内核授予的权利，包括遵守条款后的商业使用权，不受客户端非商业许可缩减。你不能仅凭本仓库的 `LICENSE` 将 sing-box 改为非商业许可，也不能在分发内核时额外要求其使用者取得 @GFVNIP 的商业授权。

若分发包含内核的完整便携包，须履行 GPL 对实际内核二进制的对应源码、必要构建材料及许可证/声明提供义务，并继续遵守内核依赖组件各自的条款。仅附上一个上游链接或 GPL 文本，不当然满足全部义务。此源码仓库未对完整便携包及内核所有传递依赖做发布合规验收。

## Windows 与 .NET Framework

客户端在 Windows 上运行，并使用系统安装的 .NET Framework 4.8 及 Win32 API。构建脚本仅引用系统程序集；本仓库不包含 Windows 或 .NET Framework 的实现、安装包或再分发组件。使用这些系统组件须遵守其供应商的适用条款。

## 许可边界

客户端与 sing-box 的许可按独立程序划分，不能将两者代码合并后继续笼统使用客户端非商业许可。如果改为复制或改编内核代码、链接 `libbox`、共享内核内部结构，或其他实质上构成单一组合程序的方式，须在分发前重新核对 GPL 适用范围并取得必要授权。现有核对事实、依据与限制见 [许可边界说明](docs/许可边界说明.md)。
