# Viv
Vivian Banshee

Viv.Log
一款轻量、灵活、开箱即用的 .NET 日志封装库，适配 NLog/log4net 主流日志框架，支持静态调用与依赖注入双模式，内置默认配置自动提取，零配置快速上手。
核心特性
🎯 多框架适配：支持 NLog/log4net，内置 NoneLogger 控制台兜底方案
🚀 零配置启动：默认配置嵌入程序集，首次使用自动提取，无需手动编写 XML
🎨 双调用模式：静态工具类 WriteLogger 极简调用，工厂类 VivLogFactory 灵活管理
🔄 可重置懒加载：配置动态更新，无需重启程序即可切换日志框架
🧩 高扩展性：基于接口 IVivLogger 设计，轻松扩展自定义日志实现
