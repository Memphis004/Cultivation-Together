Game Design Doc (Draft) — Sect Management
📌 สถานะ: เป็น Draft เรื่องการออกแบบระบบ "Sect Management" ที่เรียบร้อยตาม สถาปัตยกรรมปัจจุบันของ Vault
⚠️ ผมมีคอนเทนต์เต็มของหน้า concepts/world-events เท่านั้น หน้าอื่นยังมีโครงสร้าง (concept sketch) เท่านั้น — ประเด็นที่มี 🔶 ด้านล่างคือส่วนที่ต้องติดเติมเพื่อให้ Doc แม่น 100%

1. Vision & Context
"Cultivation Together" เป็นเกมจำลองโลกแบบ interactive (wuxia/xianxia สไตล์) ที่ผู้เล่นสวมบทบาทเป็น Sect Master ดูแลสังคมเพาะเดินร่างเล็ก

Sect ที่ต้องจัดการรวมเช่นนี้:

👤 Members → ศิษย์ (Disciples) ที่มีเลเวล/ติวัตตัพย์ต่างกัน (Outer → Inner → Elder → SectMaster)
🎒 Inventories → ของที่แต่ละคนมี, และ Sect Stockpile เป็น "คลังรวมของเชลยบ้าน"
💰 Economy → สองเงิน = SpiritStones (เงินหลัก) และ Contribution (เมอริทของศิษย์)
🔁 Sect Loop → ดึงทุ่ง (Gathering) → ของสกปรก → Crafting → ของเสริม → ช่วย/ขาย
🌍 World Drives → อีเวนท์ของโลก (โจร, ท่างขายเร่, ศิษยกรรมขอเข้ามา ฯลฯ)
สถาปัตยกรรมเป็น "ผู้เป็นความจริงเดียว (single source of truth)" (§ concepts/state-management) — ไม่สร้างค่าจริงใหม่ทุกครั้ง แต่รีด/แก้ SectEconomyState ตลอดชีวิตเกม มี DI via concepts/vcontainer-composition, เลื่อการส่งข้อความผ่าน concepts/message-pipe-bus, และประนิธ์ให้ AI/LLM ดูผ่าน concepts/mcp-bridge

การพรับเป้ามุ่งหลัก (Core Pillars)

ศิษย์ → เพิ่มคุณสมบัติ/ช่วยงาน/พัฒนาติวัตตัพย์อย่างเป็นระบบ
Spect → คลังรวมของ + ช่วงพื้นฐาน + เครื่องมือเกี่ววกับการบริหาร
Economy → การแลกเปลี่ยนเกิดความต้องการการวางแผน (ต้องใช้ Contribution, ต้องรอ Crafting)
World Drives → อีเวนท์ของโลกเป็น director ที่แทรกทุกเกม
2. Data Model (-groundedใน [[concepts/state-management]])
SectEconomyState          ← single live instance, messagepack serializable
├── Disciples: List<DiscipleState>
│   ├── DiscipleId        (d000, d001, ...)
│   ├── DisplayName
│   ├── Rank              (Outer / Inner / Elder / SectMaster)
│   ├── Wallet            (SpiritStones: long, Contribution: long)
│   ├── PersonalInventory: InventoryItem[]
│   └── CurrentTask       ("gathering_herb", "refining_elixir", ...)
└── Stockpile: SectStockpile
    ├── RawResources      Dict<string,int>  (herb, wood, ore, provisions)
    └── CraftedGoods      InventoryItem[]
        └── InventoryItem (ItemDefId, Quantity, Grade 1-5, OwnerScope)
              OwnerScope = Personal | SectStockpile
🔶 ต้องติดเติม: โครงสร้างข้อความเต็มของ InventoryItem, Enum ของ Rank และ Detail ของ disciple attribute/-stats ยังไม่มีใน Vault

3. Economy & Ownership (ที่มี spec ชัดเจน)
🔶 Core Rule: Ownership Rule
ของที่คราฟต์ จะถูกกำหนดเป้าหมายตามติวัตตัพย์ของศิษย์ครั้งเดียวกัน:

Elder / SectMaster → ไป PersonalInventory (ชื่อตน, ข้าม store)
Outer / Inner → ไป Stockpile.CraftedGoods (ของเชลยบ้าน → เอาไว้ขาย/แลก)
ชอ้ชจาก concepts/state-management และ concepts/crafting-system
💵 Pricing (placeholder — ต้อง balance)
ราคาใน store เช่น grade × 50 × quantity (ตัวอย่าง: grade 3 × 1 = 150 Contribution)
🔶 เลขนี้ในโค้ดปัจจุบันเป็น placeholder ให้ต้องแก้เป็น tunables ใน GDD
🛒 Purchase Store (§ concepts/purchase-store)
Atomic check-and-deduct: ตรวจครบอีกกี่ตัวก่อน — ถ้าไม่พอ → ไม่เปลี่ยนสถานะใดๆ เลย
อย่างสุด: Outer/Inner ค่อยซื้อได้ (จากของใน Stockpile เท่านั้น)
กลยุทธ์เรื่องเงิน: ศิษย์ต้อง攒 Contribution → เอาไปปั่นของ → เพิ่มคุณสมบัติ
4. Sect Production Loop
Gathering ─→  RawResources (herb, wood, ore, provisions)
        ├──> Crafting (CurrentTask) ──► owned by rank ──► Personal / Stockpile
        └──> Sect Stockpile ──> disciples purchase via Contribution
กฎย่อยที่มีใน architecture:

All-or-nothing — TryConsume: สร้างอีกกี่ตัว → ถ้าไม่มีพอ → รอ ไม่ตัดส่วน (§ concepts/crafting-system)
Per-disciple progress — ศิษย์คนไหนถูกตันงานเดียวกัน ค่าติดตั้งเอง (ไม่รวมกัน)
Stockpile merge — ของตัวไหนตัวเดิม (ItemDefId + Grade + OwnerScope) เรียงตอมกัน
5. World Drives & Decision (Sect reacts to the world)
อีเวนท์ที่กระทบ Sect (from concepts/world-events):

Event	ต้องตัดสินจัย	ผลกระทบ
bandit_raid_001	✅ ต้อง	-20 provisions
new_disciple_applicant	✅ ต้อง	อย่างสุด / Reject ศิษย์ใหม่
herb_garden_bloom	❌ (ข้อมูล)	+30 herb
wandering_merchant	✅ ต้อง	-20 ore, +40 provisions
Flow:
WorldEvent fires → SetPaused (decision checkpoint) → UI Popup + MCP tool → ตัดสินสุดผ่าน DecisionExecutor เดียวกัน → แก้สถานะแล้ว unpause

6. Systems / UI (current — still partial)
ระบบ	สถานะเตือนใน Vault
Resource HUD (แสดงความเปลี่ยนของ)	✅ มี (SectResourceChangedMessage)
Event Popup (ตัดสินจัย)	✅ มี
Disciple List (รายการศิษย์/inventory)	🔶 มิถือว่า เสร็จ — มี TODO
Crafting/Gathering dashboard	🔶 ต้องเพิ่ม
Time / Save-load	🔶 ยังไม่ได้ (TimeSystem Tick() ยัง no-op)
7. MCP Expose (AI/Game-Master visibility)
Tool	Role
get_sect_state	Read กระจกสถานะเต็ม
await_next_world_event	Read Blocking รอ/เห็นอีเวนท์
execute_decision	Write ตอบกลับอีเวนท์ที่ต้องตัดสิน
purchase_item	Write ตัดสินซื้อ
8. Open Questions / TODO (ต้องติดเติมก่อน)
🔶 WalletChangedMessage ยังไม่มี (HUD ต้อง Piggyback บน resource event) → ถ้าตัดสอนเงินวันนี้ หัวใจจะ desync (§ concepts/state-management §Purchase Store — มี ⏳ TODO)
🔶 อีเวนท์อยู่ระดับพื้นฐาน — ขาด state-conditional, cooldown, chaining, priority
🔶 Recipes/Price เป็น number placeholders → ต้องตั้ง balance
🔶 Crafting/Gathering/Disciple UI panel ยังไม่มี
9. ต่อไป (Build vs Design)
การเข้าสู่ architecture ต้องทำตามลำดับ:

VContainer/DI ──> MessagePipe ──> Single State Instance ──> Systems
        └──> MCP Bridge (AI/GM)  (Request/Response + Pub/Sub)
✅ สรุป
Doc นี้ เรียบร้อยตามสถาปัตยกรรมปัจจุบัน โดยเน้น: 1) single-state truth, 2) ownership rule ตาม rank, 3) Atomic purchase/consume, 4) World-event-driven decision loop, 5) AI/MCP visibility

Sources
State Management — โมเดล SectEconomyState, single live instance, AdjustAndNotify, serialization
Crafting System — recipes, per-disciple progress, ownership rule, all-or-nothing consume
Purchase Store — Contribution pricing, atomic check-and-deduct, buying rules
World Events — 4 อีเวนท์ปัจจุบัน + ผลกระทบ + สถานะ "What's Missing"
Decision Pipeline — auto-pause checkpoint, เดียว DecisionExecutor เดียวกัน, AI/GM flow
MCP Bridge — tools (get_sect_state, purchase_item, execute_decision, await_next_world_event)
MessagePipe Bus — pub/sub + request/response backbone
VContainer Composition — DI single source of truth
🎯 ปัญหาส่วน: ผมยังไม่มี [[entities/*]], craft, หรือหน้าเต็มอื่นของระบบก่อสร้าง — ถ้าอยากให้ GDD นี้ เต็ม 100% ให้อัปโหลดหน้า Disciples, Items, และ crafting เต็มไว้แล้วผมจะเติม Data Model, Gathering, และ Crafting detail ลงไปให้

อยากให้ผมติดหรือค่อยสร้าง GDD เต็มให้หนึ่งระบบตามลำดับไหม?