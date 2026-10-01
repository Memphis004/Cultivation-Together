using System.Runtime.CompilerServices;

// Composition-root installers (Core/Installers/*) เป็น internal โดยเจตนา — พวกมันคือ
// รายละเอียดการ wiring ของ GameLifetimeScope ไม่ใช่ API ของเกม ให้ EditMode test เข้าถึง
// เพื่อยืนยัน "ของที่ผูกกับฉากอยู่ที่ SectSceneLifetimeScope เท่านั้น" ระดับ container จริง
// (RootScopeContainerTests / ChildScopeContainerTests) โดยไม่ต้องเปิด public API เพิ่ม
[assembly: InternalsVisibleTo("Xianxia.Sect.Tests.EditMode")]
