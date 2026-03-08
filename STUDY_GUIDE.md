# 📚 STUDY GUIDE - Senior C# Developer Interview

## 1. OOP - Lập trình Hướng Đối Tượng

### Abstract Class vs Interface
```
BaseDevice (abstract class):          IConnectable (interface):
- Có implementation                   - Chỉ có khai báo
- protected fields                    - Không có fields
- Dùng khi có code chung              - Dùng khi muốn "contract"
- Single inheritance                  - Multiple interface
```

**Điểm quan trọng:**
- `TcpDevice : BaseDevice` → kế thừa abstract class
- `BaseDevice : IConnectable, IDataReader` → implement nhiều interface
- `sealed` Logger → không thể kế thừa
- `override` GetDeviceInfo() trong SerialDevice

---

## 2. MULTI-THREADING

### Tại sao cần multi-thread?
> Khi app cần đọc dữ liệu từ 10 thiết bị cùng lúc, nếu dùng 1 thread → UI bị đóng băng.

### Các vấn đề quan trọng:

**Thread Safety - Race Condition:**
```csharp
// SAI - không thread-safe:
if (_deviceThreads.ContainsKey(id))
    _deviceThreads.Remove(id);  // Thread khác có thể chen vào đây!

// ĐÚNG - dùng lock:
lock (_lock) {
    if (_deviceThreads.ContainsKey(id))
        _deviceThreads.Remove(id);
}
```

**CancellationToken - Dừng thread an toàn:**
```csharp
// ĐỪNG dùng thread.Abort() - nguy hiểm!
// DÙNG CancellationTokenSource:
var cts = new CancellationTokenSource();
cts.Cancel();  // Báo thread nên dừng
token.WaitHandle.WaitOne(1000);  // Interruptible sleep
```

**InvokeRequired - UI từ background thread:**
```csharp
// Luôn dùng khi update UI từ thread khác!
void UpdateLabel(string text) {
    if (label.InvokeRequired)
        label.BeginInvoke((Action)(() => label.Text = text));
    else
        label.Text = text;
}
```

**Các loại lock:**
```
lock (_obj)              → Monitor - basic
ReaderWriterLockSlim     → Nhiều reader, 1 writer (Logger dùng)
ConcurrentQueue<T>       → Thread-safe queue (không cần lock)
ConcurrentDictionary<>   → Thread-safe dictionary
volatile                 → Ngăn compiler optimize biến check
```

### Producer-Consumer Pattern (WorkerPool):
```
Thread 1 (Producer) → [Queue] → Thread 2,3,4 (Consumer workers)
```

---

## 3. TCP/IP SOCKET

### Flow hoạt động:

**Server:**
```
TcpListener.Start()
    └─> AcceptLoop thread: TcpListener.AcceptTcpClient() [blocking]
            └─> Mỗi client: thread riêng → HandleClient()
                    └─> stream.Read() → parse → raise event
```

**Client:**
```
TcpClient.Connect()
    ├─> ReceiveThread: stream.Read() loop
    └─> HeartbeatThread: gửi PING mỗi 30s
```

**Các concept cần nhớ:**
- `TcpListener` = Server socket
- `TcpClient` = Client socket
- `NetworkStream` = đọc/ghi data
- `Encoding.UTF8.GetBytes()` = convert string → bytes
- `ConcurrentDictionary` để quản lý nhiều client thread-safe

---

## 4. RS-232 SERIAL COMMUNICATION

### Thông số RS-232:
```
BaudRate: 9600, 19200, 57600, 115200 bps
DataBits: 7 hoặc 8
Parity: None, Even, Odd
StopBits: One, Two
Flow Control: RTS/CTS hoặc None
```

### 2 cách đọc Serial:
```csharp
// 1. Event-driven (khuyến khích):
port.DataReceived += Port_DataReceived;  // Tự động gọi khi có data

// 2. Polling (blocking):
string line = port.ReadLine();  // Chờ đến khi có dòng hoàn chỉnh
```

### Buffer overflow problem:
> Serial data đến từng chunk nhỏ, phải dùng StringBuilder buffer
> và chờ ký tự kết thúc (\n) mới xử lý.

---

## 5. PATTERNS QUAN TRỌNG

| Pattern | File | Dùng để |
|---|---|---|
| **Singleton** | Logger.cs | Chỉ 1 instance trong app |
| **Abstract Class** | BaseDevice.cs | Template method |
| **Observer/Event** | Mọi file | Notify UI khi có data |
| **Producer-Consumer** | WorkerPool.cs | Task queue |
| **Template Method** | BaseDevice.Connect() | Skeleton algorithm |
| **DTO** | SensorData.cs | Truyền data giữa layers |

---

## 6. CODE REVIEW CHECKLIST (dùng khi review team)

```
□ Thread safety: có lock khi access shared resources không?
□ InvokeRequired: UI update từ background thread?
□ Dispose: có implement IDisposable và cancel threads?
□ Exception handling: catch đủ chỗ, không swallow exception?
□ Null check: ArgumentNullException cho constructor params?
□ Naming: PascalCase class/method, camelCase variable?
□ XML Comments: có /// summary cho public methods?
□ Magic numbers: dùng const thay vì hardcode?
```

---

## 7. CÂU HỎI PHỎNG VẤN THƯỜNG GẶP

**Q: Deadlock là gì? Cách tránh?**
> Xảy ra khi 2 thread chờ nhau giải phóng lock. Tránh bằng: luôn lock theo thứ tự nhất định, dùng timeout, tránh nested lock.

**Q: volatile vs lock khác gì?**
> `volatile` chỉ đảm bảo visibility (không cache), `lock` đảm bảo cả visibility lẫn atomicity.

**Q: Background thread vs Foreground thread?**
> Background (`IsBackground = true`): app đóng thì thread tự chết. Foreground: app chờ thread kết thúc.

**Q: async/await vs Thread khác gì?**
> `async/await` dùng ThreadPool, không block thread, phù hợp I/O. Thread manual dùng khi cần kiểm soát priority/affinity.

**Q: Khi nào dùng ConcurrentDictionary thay vì Dictionary + lock?**
> ConcurrentDictionary tốt hơn cho read-heavy. Dictionary + lock tốt hơn khi cần atomic multi-step operations.
