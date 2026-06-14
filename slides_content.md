# Slide thuyết trình — Optigo
## Hệ thống gợi ý điểm đến và tối ưu hóa chuỗi hành trình nhóm thời gian thực

> Mỗi trang slide dưới đây khớp chính xác 100% với nội dung và cấu trúc chữ hiển thị trong file slide báo cáo thực tế (PDF).

---

## Slide 1 — Trang bìa

**Hệ Thống Gợi Ý Điểm Đến & Tối Ưu Chuỗi Hành Trình Nhóm**

- Sinh viên: **Trần Quang Thành**
- Mã sinh viên: **23020155**
- Giảng viên HD: **ThS. Trần Mạnh Cường**
- Báo cáo môn: **Dự án công nghệ — ĐHCN, ĐHQGHN**

---

## Slide 2 — Nội dung trình bày

**Nội dung trình bày**

- **1. Đặt Vấn Đề**: Khó khăn khi chọn điểm hẹn nhóm
- **2. Ý Tưởng**: Giải pháp tối ưu hóa bằng số liệu
- **3. Phương Pháp**: Trọng tâm: Pipeline thực hiện
- **4. Đánh Giá**: Kết quả so sánh thực nghiệm
- **5. Kết Luận**: Tổng kết và hướng phát triển

---

## Slide 3 — Bối cảnh: Chọn điểm hẹn nhóm

**Bối cảnh: Chọn điểm hẹn nhóm**

Một tình huống quen thuộc:
- Mỗi người ở một nơi, sử dụng phương tiện khác nhau.
- Có người cần được đón, có người có thể tự đi.
- Bàn bạc qua group chat thường dẫn đến quyết định bằng cảm tính.
- Kết quả: Thiếu công bằng, tốn thời gian, và có thể gây bất tiện lớn cho một vài thành viên.

---

## Slide 4 — Vấn đề cụ thể

**Vấn đề cụ thể**

- Không phải ở giữa là tốt nhất
- Không phải đánh giá cao là phù hợp nhất
- Cần tối ưu đón ai – dừng đâu – đi thế nào

---

## Slide 5 — Hạn chế của các giải pháp hiện tại

**Hạn chế của các giải pháp hiện tại**

| Tiêu chí | Bản đồ (Google Maps) | Nhóm chat (Zalo/Mess) | Ứng dụng gọi xe |
| :--- | :--- | :--- | :--- |
| **Ưu điểm** | Dữ liệu địa điểm phong phú | Linh hoạt, tức thời | Điều phối tài xế chuyên nghiệp |
| **Nhược điểm chính** | Không tối ưu cho nhóm nhiều người | Quyết định cảm tính, dễ thiên vị | Không hỗ trợ tự chọn điểm gặp |
| **Tính toán công bằng** | ❌ | ❌ | ❌ |

---

## Slide 6 — Optigo giải bài toán gì?

**Optigo giải bài toán gì?**

Không chỉ trả lời "Đi đâu?" mà còn "Đi như thế nào?"

```
[1] Nhu cầu gặp mặt ──> [2] Thông tin thành viên ──> [3] Địa điểm ứng viên
                                                               │
[6] Đề xuất cuối cùng <── [5] Lộ trình và chi phí <── [4] Phương án ghép đón
```

---

## Slide 7 — Kiến trúc hệ thống tổng quan

**Kiến trúc hệ thống tổng quan**

- **Frontend**: Next.js & React. Hiển thị bản đồ Mapbox, quản lý phòng nhóm, giao diện bình chọn và chat. Tương tác thời gian thực.
- **Backend**: ASP.NET Core với kiến trúc CQRS. Xử lý logic nghiệp vụ, Routing Core, và đồng bộ trạng thái qua SignalR.
- **Data & APIs**: Lưu trữ PostgreSQL. Tích hợp sâu với Google Places API để lấy POI và Google Routes API để tính ma trận khoảng cách.

---

## Slide 8 — Pipeline Tối Ưu Phân Tầng

**Pipeline Tối Ưu Phân Tầng**

- **Các tầng đầu**: Sử dụng phép ước lượng nhanh để thu hẹp không gian tìm kiếm từ hàng chục xuống khoảng 15 ứng viên.
- **Các tầng sau**: Xử lý sâu từng bài toán con (sinh điểm đón, phân công, lập tuyến) để đảm bảo độ chính xác và tính công bằng.

```
[Dữ liệu phiên] ──> [Tìm kiếm tâm có trọng số] ──> [Địa điểm ứng viên]
                                                          │
[Chi phí công bằng và Pareto] <── [Phân công HK-TX] <── [Sinh điểm đón & điểm đón chung] <── [Sàng lọc sơ bộ lộ trình]
```

---

## Slide 9 — Bước 1: Tâm tìm kiếm có trọng số

**Bước 1: Tâm tìm kiếm có trọng số**

Thay vì lấy trung bình tọa độ dễ bị kéo lệch bởi người ở xa, Optigo sử dụng trung vị hình học có trọng số (Weiszfeld).

- **Người đi bộ**: Trọng số cao nhất vì khả năng di chuyển bị hạn chế nhất.
- **Người cần đón**: Trọng số cao vì phụ thuộc vào tài xế.
- **Ô tô / Xe máy**: Trọng số thấp hơn do tính cơ động cao.
- **Kết quả**: Tạo ra một vùng truy vấn ổn định và đại diện tốt hơn cho cả nhóm.

---

## Slide 10 — Bước 2 & 3: Thu nhập và Sàng lọc

**Bước 2 & 3: Thu nhập và Sàng lọc**

- Từ tâm Weiszfeld, hệ thống truy vấn Google Places API lấy ra khoảng 50 ứng viên địa điểm sơ bộ.
- Hệ thống chỉ giữ lại khoảng 15 địa điểm có điểm chi phí tốt nhất để tiếp tục tối ưu sâu, giúp tiết kiệm tài nguyên tính toán.

---

## Slide 11 — Bước 4: Sinh điểm đón

**Bước 4: Sinh điểm đón**

| Loại điểm đón | Mô tả |
| :--- | :--- |
| **Tại cửa** | Đón ngay tại vị trí xuất phát. Hành khách thoải mái nhưng tài xế có thể phải đi vòng nhiều. |
| **Điểm gần (POI)** | Các mốc dễ nhận biết trong bán kính đi bộ (ví dụ: siêu thị, ngã tư). |
| **Hành lang** | Điểm chiếu từ hành khách lên lộ trình của tài xế, giúp tài xế gần như không phải đi vòng. |
| **Định hướng** | Điểm nằm trên hướng di chuyển về phía tài xế hoặc địa điểm gặp. |
| **Điểm đón chung** | Gom các hành khách ở gần nhau đi bộ tới một điểm, giảm số lần dừng của tài xế. |

---

## Slide 12 — Đánh đổi: Đi bộ vs Đi vòng

**Đánh đổi: Đi bộ vs Đi vòng**

Khi tài xế đi đón hành khách tại điểm s trước khi đến đích v, chi phí đi vòng được đo bằng:

$$\text{detour\_lb}(d, s, v) = T(d \to s) + T(s \to v) - T(d \to v)$$

Trong đó:
- $d$ = vị trí hiện tại của tài xế
- $s$ = điểm đón hành khách
- $v$ = địa điểm cuối cùng (destination)
- $T(A \to B)$ = thời gian di chuyển từ A đến B

**Ý nghĩa**: Nếu $\text{detour\_lb} \approx 0$, điểm đón nằm trên đường đi của tài xế (không đi vòng). Nếu $\text{detour\_lb}$ lớn, tài xế phải đi vòng nhiều $\to$ cần chọn điểm đón khác.

---

## Slide 13 — Bước 5: Phân công hành khách

**Bước 5: Phân công hành khách**

Thuật toán tham lam (gán người gần nhất) dễ dẫn đến kết quả kém vì không xét chi phí cơ hội.

- **Sắp xếp theo độ tiếc nuối**: Chênh lệch giữa lựa chọn tốt nhất và tốt thứ hai. Ai có ít lựa chọn tốt sẽ được ưu tiên xếp trước.
- **Branch-and-Bound**: Duyệt các khả năng dưới dạng cây quyết định.
- **Cắt tỉa (Pruning)**: Loại bỏ sớm những nhánh phân công có cận dưới chi phí tệ hơn nghiệm hiện tại, đảm bảo giữ lại tập phương án tối ưu.

---

## Slide 14 — Bước 6: Tối ưu tuyến (Open-path TSP)

**Bước 6: Tối ưu tuyến (Open-path TSP)**

**Sắp xếp thứ tự đón khách**

Sau khi chốt người và điểm đón, bài toán chuyển thành TSP mở (Open-path TSP) từ vị trí tài xế đến điểm gặp.

**Giải pháp**:
- Nhóm nhỏ ($\le$ 9 điểm): Quy hoạch động Held-Karp (tối ưu chính xác).
- Nhóm lớn: Heuristic cheapest insertion + 2-opt.
- **Tập hợp lộ trình**: Sinh nhiều tập con hành khách cho mỗi tài xế và chọn tổ hợp bao phủ tốt nhất.

---

## Slide 15 — Bước 7: Đánh giá gánh nặng (Burden)

**Bước 7: Đánh giá gánh nặng (Burden)**

Chỉ số Gánh nặng di chuyển ($B$) đo lường mức độ bất tiện tổng hợp của từng thành viên:

- **Tài xế**: $B_d = T_{\text{drive}} + \lambda_\Delta \max(0, T_{\text{route}} - T_{\text{direct}}) + \lambda_s N_{\text{stops}}$
  - $T_{\text{drive}}$: Thời gian lái xe thực tế
  - $T_{\text{route}} - T_{\text{direct}}$: Thời gian đi vòng phát sinh (Detour)
  - $N_{\text{stops}}$: Số điểm dừng đón khách
  - $\lambda_\Delta, \lambda_s$: Hệ số phạt đi vòng và dừng đỗ
- **Hành khách**: $B_p = T_{\text{ride}} + \lambda_w T_{\text{walk}} + \lambda_q T_{\text{wait}} + R_{\text{access}}$
  - $T_{\text{ride}}$: Thời gian ngồi xe di chuyển
  - $T_{\text{walk}}, T_{\text{wait}}$: Thời gian đi bộ và chờ đợi
  - $\lambda_w, \lambda_q$: Hệ số phạt đi bộ ($\lambda_w=2.0$) và chờ đợi ($\lambda_q=1.5$)
  - $R_{\text{access}}$: Rủi ro tiếp cận điểm đón

**Ý nghĩa**: Là cơ sở để đánh giá mức độ bất bình đẳng (công bằng Gini) của phương án di chuyển.

---

## Slide 16 — Biên Pareto & Nhãn Giải Thích

**Biên Pareto & Nhãn Giải Thích**

Mục tiêu: Chọn ra các phương án đại diện cho những kiểu đánh đổi tốt nhất.

- **Lọc Pareto**: Loại bỏ phương án bị phương án khác tốt hơn trên mọi tiêu chí.
- **Giữ lại lựa chọn tốt theo từng ưu tiên**
- **Gắn nhãn**: Nhanh nhất, Công bằng nhất, Ít đi bộ vòng, Cân bằng.
- **Giúp nhóm chọn phương án phù hợp** thay vì chỉ tin vào một điểm số.

---

## Slide 17 — Phương án đầu ra trực quan

**Phương án đầu ra trực quan**

- Giao diện hiển thị rõ ràng trên bản đồ: tuyến đi của từng tài xế, điểm đón đề xuất, cùng thẻ thông tin về thời gian và nhãn giải thích.
- Tất cả thao tác bình chọn được đồng bộ thời gian thực cho toàn nhóm.

---

## Slide 18 — Thiết lập thực nghiệm

**Thiết lập thực nghiệm**

- **Bộ dữ liệu đối sánh**: Tổng cộng 120 kịch bản với ~10 thành viên/kịch bản. Gồm 96 kịch bản từ DARP-MP (gần với bài toán Optigo) và 24 kịch bản khó từ bài toán Li-Lim.
- **Phương pháp so sánh**: Chạy thử nghiệm so sánh Optigo với các bộ giải nổi tiếng: OR-Tools (Google), PyVRP, và VROOM trên cùng một cấu hình phần cứng.
- **Tiêu chí đánh giá**: Đánh giá toàn diện qua: Tỷ lệ tìm được nghiệm hợp lệ, Chi phí, Độ công bằng (Gini), Gánh nặng lớn nhất, Độ tiếc nuối và Thời gian đi vòng.

---

## Slide 19 — Kết quả tổng quan

**Kết quả tổng quan**

| Phương án | Hợp lệ | Chi phí TB | Công bằng TB | Gánh nặng TB lớn nhất | Xử lý TB (ms) |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **Optigo** | **95.8%** | **5269** | **826** | **1359** | **953** |
| OR-Tools | 90.8% | 5518 | 999 | 1484 | 8007 |
| VROOM | 81.7% | 5727 | 1199 | 1611 | 3843 |
| PyVRP | 81.7% | 5711 | 1199 | 1611 | 9210 |

**Optigo**: hợp lệ cao nhất, chi phí thấp nhất, công bằng tốt nhất

---

## Slide 20 — So sánh trực tiếp

**So sánh trực tiếp**

**Optigo cải thiện công bằng mà vẫn giữ chi phí cạnh tranh**

| So với | Số kịch bản | Công bằng tốt hơn | Chi phí tốt hơn | Chênh lệch chi phí TB | Cải thiện công bằng |
| :--- | :---: | :---: | :---: | :---: | :---: |
| OR-Tools | 109 | 95/109 | 72/109 | -3.9% | 14.7% |
| PyVRP | 98 | 92/98 | 78/98 | -5.8% | 22.3% |
| VROOM | 98 | 92/98 | 78/98 | -5.7% | 22.2% |

- Trên DARP-MP, Optigo đạt 96/96 kịch bản hợp lệ.
- Trên Li-Lim, Optigo vẫn đạt 19/24, cao hơn các phương án cơ sở.
- Điểm công bằng và gánh nặng cá nhân đều thấp hơn rõ rệt.

---

## Slide 21 — Demo

**Demo**

---

## Slide 22 — Cảm ơn thầy đã lắng nghe!

**CẢM ƠN THẦY ĐÃ LẮNG NGHE!**
