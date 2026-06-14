# Script thuyết trình — Optigo
## Hệ thống gợi ý điểm đến và tối ưu hóa chuỗi hành trình nhóm thời gian thực

> Mỗi phần kịch bản dưới đây tương ứng khớp chính xác với từng trang trong bộ slide 22 trang (PDF). Nội dung nói trình bày trực tiếp nội dung chuyên môn, loại bỏ hoàn toàn các câu giới thiệu mang tính mô tả slide ("Slide này thể hiện...", "Nhìn vào slide...", "Như trên hình..."), đảm bảo tính tự nhiên, liên tục và thời lượng khoảng **15 phút**.

---

## Slide 1 — Trang bìa (~20 giây)

Kính chào thầy cô và các bạn thành viên Hội đồng. Em là Trần Quang Thành, mã số sinh viên 23020155. Hôm nay em xin phép được báo cáo đề tài khóa luận tốt nghiệp: **"Hệ Thống Gợi Ý Điểm Đến & Tối Ưu Chuỗi Hành Trình Nhóm"**, dưới sự hướng dẫn khoa học của thầy ThS. Trần Mạnh Cường. Sau đây em xin bắt đầu phần trình bày của mình.

---

## Slide 2 — Nội dung trình bày (~15 giây)

Nội dung báo cáo ngày hôm nay sẽ đi theo đúng bố cục 5 phần tiêu chuẩn của một dự án công nghệ, bao gồm:
1. **Đặt Vấn Đề**: Phân tích những khó khăn thực tế khi chọn địa điểm và di chuyển nhóm.
2. **Ý Tưởng**: Giải pháp tối ưu hóa hành trình bằng số liệu khách quan.
3. **Phương Pháp**: Trọng tâm là pipeline thuật toán phân tầng mà em đề xuất.
4. **Đánh Giá**: Kết quả so sánh thực nghiệm với các bộ giải quốc tế.
5. **Kết Luận**: Tổng kết dự án và định hướng nghiên cứu tiếp theo.

---

## Slide 3 — Bối cảnh: Chọn điểm hẹn nhóm (~50 giây)

Việc tổ chức gặp mặt nhóm là một tình huống cực kỳ quen thuộc trong đời sống hàng ngày. Tuy nhiên, quy trình thảo luận nhóm trong các group chat hiện nay thường rất lộn xộn và tốn thời gian. Mỗi người xuất phát từ một nơi khác nhau, sử dụng phương tiện khác nhau, và đặc biệt là trong nhóm thường có sự phân hóa vai trò: có người cần được đón, có người có thể tự đi.

Khi bàn bạc, nhóm thường quyết định dựa trên cảm tính. Kết quả của việc này là sự thiếu công bằng. Một người có thể chỉ mất 5 phút di chuyển, trong khi một người khác phải đi đến 30–40 phút, hoặc một tài xế phải đi vòng quãng đường rất xa để đón bạn mà cả nhóm không hề nhận thức được điều đó. Điều này gây bất tiện lớn cho một vài thành viên và làm giảm trải nghiệm chung của toàn nhóm.

---

## Slide 4 — Vấn đề cụ thể (~50 giây)

Đi sâu hơn vào mặt kỹ thuật, việc chọn địa điểm gặp mặt nhóm tối ưu gặp phải ba thách thức cụ thể:
- **Thứ nhất, không phải ở giữa là tốt nhất**: Trung bình tọa độ địa lý (Centroid) rất dễ bị kéo lệch bởi các thành viên ở quá xa (outliers). Hơn nữa, khoảng cách đường thẳng trên bản đồ hoàn toàn khác xa thời gian di chuyển thực tế trên mạng lưới đường đô thị vốn bị ảnh hưởng bởi đường một chiều, ùn tắc hay cầu cống.
- **Thứ hai, không phải địa điểm được đánh giá cao là phù hợp nhất**: Một quán cà phê có đánh giá 5 sao nhưng nằm ở vị trí gây kẹt xe hoặc bắt đa số thành viên phải di chuyển quá xa thì không phải là lựa chọn tối ưu cho nhóm.
- **Thứ ba, cần tối ưu hóa ghép đón**: Hệ thống bắt buộc phải giải quyết đồng thời câu hỏi: ai đón ai, dừng ở đâu để đón, và đi theo tuyến đường nào. Đây là bài toán tích hợp của Dial-a-Ride (DARP) và lập tuyến (VRP), có độ phức tạp tính toán cực kỳ lớn.

---

## Slide 5 — Hạn chế của các giải pháp hiện tại (~45 giây)

Các giải pháp hiện nay trên thị trường đều tồn tại những hạn chế rất rõ rệt:
- **Bản đồ (như Google Maps)**: Có dữ liệu địa điểm phong phú nhưng chỉ phục vụ định tuyến đơn lẻ cho cá nhân, hoàn toàn không tối ưu cho nhóm nhiều người.
- **Nhóm chat (Zalo/Messenger)**: Rất linh hoạt và tức thời, nhưng quyết định cuối cùng vẫn cảm tính, thiếu tính toán khoa học và dễ thiên vị cho người đưa ra đề xuất đầu tiên.
- **Ứng dụng gọi xe (Grab/Be)**: Giải quyết tốt khâu điều phối tài xế chuyên nghiệp, nhưng không hỗ trợ nhóm tự chọn điểm gặp và tự điều phối phương tiện cá nhân với nhau.
- Điểm chung là cả ba giải pháp này đều **hoàn toàn không hỗ trợ tính toán công bằng** về gánh nặng di chuyển cho các thành viên. Đó là lý do em xây dựng Optigo.

---

## Slide 6 — Optigo giải bài toán gì? (~50 giây)

Mục tiêu của Optigo là chuyển câu hỏi cảm tính "Đi đâu?" thành một bài toán tối ưu có số liệu hỗ trợ. Hệ thống không chỉ gợi ý địa điểm gặp mặt, mà trả về một **giải pháp hành trình trọn gói**. 

Luồng hoạt động của Optigo diễn ra thông qua 6 bước chính:
- Đầu tiên, hệ thống tiếp nhận **Nhu cầu gặp mặt** của nhóm.
- Tiếp theo, thu thập **Thông tin thành viên** gồm vị trí xuất phát và vai trò của họ.
- Trên cơ sở đó, tìm kiếm các **Địa điểm ứng viên** phù hợp.
- Lập **Phương án ghép đón** tối ưu (quyết định ai đón ai, đón ở đâu).
- Tính toán chi tiết **Lộ trình và chi phí** thời gian, công bằng của từng thành viên.
- Cuối cùng, đưa ra **Đề xuất cuối cùng** trên biên Pareto để nhóm đưa ra quyết định dựa trên dữ liệu rõ ràng.

---

## Slide 7 — Kiến trúc hệ thống tổng quan (~45 giây)

Để triển khai hệ thống này hoạt động ổn định và đáp ứng thời gian thực, kiến trúc hệ thống được thiết kế gồm 3 thành phần chính:
- **Frontend**: Dựng bằng Next.js và React, tích hợp bản đồ Mapbox GL. Phần này quản lý phòng nhóm, hiển thị trực quan các lộ trình đi chung xe của tài xế và hành khách, đồng thời cung cấp giao diện biểu quyết và chat thời gian thực.
- **Backend**: Xây dựng bằng ASP.NET Core theo mô hình CQRS để đảm bảo hiệu năng cao. Đây là nơi xử lý toàn bộ logic thuật toán tối ưu hóa lõi. Backend đồng bộ hóa trạng thái phòng tức thời cho tất cả các thiết bị thông qua công nghệ **SignalR**.
- **Data & APIs**: Sử dụng PostgreSQL để lưu trữ thông tin phiên. Hệ thống tích hợp sâu với Google Places API để truy vấn các POI chất lượng và Google Routes API để tính toán ma trận khoảng cách/thời gian thực tế trên mạng đường.

---

## Slide 8 — Pipeline Tối Ưu Phân Tầng (~90 giây)

Trọng tâm khoa học của đề tài là việc đề xuất **Pipeline Tối Ưu Phân Tầng** gồm 7 bước. 

Về mặt lý thuyết, việc tích hợp chọn điểm gặp, sinh điểm đón, phân công hành khách cho tài xế và tối ưu hóa thứ tự dừng đón tạo ra một không gian trạng thái khổng lồ mang tính chất bùng nổ tổ hợp. Độ phức tạp tổng quát của bài toán này nếu giải trực tiếp là $O(M \cdot 5^K \cdot N^K \cdot s!)$ (với $M$ địa điểm, $K$ hành khách, $N$ tài xế, $s$ điểm dừng). Điều này khiến bài toán trở thành NP-khó và không thể giải quyết trong thời gian thực dưới 2 giây.

Để giải quyết thách thức này, pipeline phân tầng hoạt động theo nguyên lý chia để trị. Các tầng đầu tiên thực hiện các phép ước lượng nhanh bằng hình học để lọc thô, thu hẹp nhanh chóng không gian từ khoảng 50 địa điểm ban đầu xuống còn 15 ứng viên tốt nhất. Các tầng sau mới tiến hành chạy các thuật toán tối ưu hóa sâu trên từng bài toán con đối với 15 địa điểm rút gọn này, bao gồm: lọc lộ trình sơ bộ, sinh điểm đón, phân công hành khách cho tài xế bằng nhánh cận, và tính toán biên Pareto để xếp hạng.

---

## Slide 9 — Bước 1: Tâm tìm kiếm có trọng số (~75 giây)

Bước khởi đầu của pipeline là xác định vùng địa lý tối ưu để bắt đầu tìm kiếm địa điểm gặp. Thay vì sử dụng trung bình tọa độ vốn rất nhạy cảm với các điểm ở xa, Optigo áp dụng thuật toán lặp **Weiszfeld** để tìm **Trung vị hình học có trọng số (Weighted Geometric Median)**.

Hệ thống gán trọng số động $w_i$ phản ánh khả năng di chuyển và mức độ bất tiện của từng thành viên:
- **Người đi bộ**: Được ưu tiên cao nhất với trọng số $3.0$ vì họ không có phương tiện chủ động.
- **Người cần đón**: Có trọng số $2.0$ do phụ thuộc vào hành trình của tài xế khác.
- **Tài xế xe máy ($1.5$)** và **Tài xế ô tô ($1.0$)**: Có trọng số thấp hơn do tính cơ động cao hơn.
Thuật toán Weiszfeld sẽ chạy lặp để hội tụ về điểm có tổng khoảng cách có trọng số ngắn nhất. Nhờ vậy, tâm tìm kiếm luôn nằm gần các thành viên yếu thế hơn trong nhóm, đảm bảo tính công bằng ngay từ bước đầu tiên.

---

## Slide 10 — Bước 2 & 3: Thu nhập và Sàng lọc (~50 giây)

Từ tâm tìm kiếm Weiszfeld vừa xác định, hệ thống truy vấn Google Places API để thu nhập khoảng 50 địa điểm ứng viên xung quanh. 

Sau đó, hệ thống thực hiện sàng lọc sơ bộ bằng cách đánh giá nhanh địa điểm bằng chi phí mạng đường ước lượng (RouteAwareVenuePrefilter). Với thành viên tự đi, hệ thống tính thời gian đi trực tiếp tới địa điểm. Với tài xế, hệ thống xét thời gian tới địa điểm và chi phí ước lượng liên quan tới các hành khách đã được chấp nhận. Nếu còn yêu cầu ghép đón chưa phân công, địa điểm sẽ bị cộng điểm phạt để phản ánh độ bất định. Kết quả chỉ giữ lại khoảng 15 địa điểm tốt nhất. Bước sàng lọc này giúp loại bỏ sớm các địa điểm không khả thi (ví dụ quán nằm ở ngõ cụt hoặc đường một chiều khó tiếp cận), tiết kiệm đến 70% tài nguyên tính toán định tuyến chi tiết.

---

## Slide 11 — Bước 4: Sinh điểm đón (~70 giây)

Để tối ưu hóa trải nghiệm ghép đón, Optigo đề xuất 5 loại điểm đón linh hoạt nhằm dung hòa sự thuận tiện của hành khách và chi phí đi vòng của tài xế:
1. **Tại cửa**: Đón ngay tại vị trí xuất phát. Tiện lợi nhất cho hành khách nhưng tài xế dễ phải đi vòng nhiều vào ngõ hẹp.
2. **Điểm gần (POI)**: Đề xuất hành khách đi bộ ngắn đến một siêu thị hay cửa hàng tiện lợi gần đó, giúp tài xế dễ tiếp cận và dừng xe.
3. **Hành lang**: Chiếu tọa độ hành khách lên tuyến đường ngắn nhất nối tài xế với điểm gặp. Đây là điểm đón rất tối ưu vì tài xế không phải đi vòng thêm.
4. **Định hướng**: Điểm nằm trên hướng di chuyển chung về phía tài xế hoặc điểm gặp, phân bổ quãng đường đi bộ.
5. **Điểm đón chung**: Sử dụng thuật toán gom cụm (Clustering) để gộp các hành khách ở gần nhau đi bộ tới một điểm tập kết duy nhất, giảm số lần dừng đỗ và thời gian phục vụ của tài xế.

---

## Slide 12 — Đánh đổi: Đi bộ vs Đi vòng (~50 giây)

Để đánh giá tính hiệu quả khi đi chung xe, chi phí đi vòng thêm của tài xế được định lượng bằng công thức cận dưới đi vòng (detour_lb). Chi phí này được tính bằng tổng thời gian đi từ vị trí tài xế $d$ đến điểm đón $s$, cộng thời gian từ $s$ đến điểm gặp $v$, trừ đi thời gian tài xế đi thẳng.

Ý nghĩa của công thức này là định lượng phần chi phí tăng thêm do việc đón khách gây ra. Nếu detour_lb tiến dần về 0, điểm đón nằm ngay trên tuyến đường di chuyển của tài xế và không gây ra sự lãng phí thời gian. Hệ thống áp dụng bộ lọc: quãng đường đi bộ của hành khách phải nhỏ hơn hoặc bằng 500m (tương đương 8 phút đi bộ). Nếu điểm đón nào vi phạm giới hạn này hoặc làm detour của tài xế tăng quá cao, hệ thống sẽ loại bỏ để tìm điểm đón tối ưu hơn.

---

## Slide 13 — Bước 5: Phân công hành khách (~75 giây)

Khi đã có các điểm đón khả thi, hệ thống giải quyết bài toán gán hành khách cho các tài xế. Thuật toán tham lam (gán khách cho xe gần nhất) thường mang lại kết quả kém do không xét chi phí cơ hội. 

Optigo tiếp cận bài toán bằng hai kỹ thuật tiến tiến:
- **Sắp xếp theo độ tiếc nuối (Regret-based ordering)**: Độ tiếc nuối được tính bằng hiệu số chi phí giữa tài xế tốt thứ hai và tài xế tốt nhất đối với hành khách đó. Hành khách có độ tiếc nuối lớn (tức là người có rất ít lựa chọn tài xế phù hợp) sẽ được ưu tiên phân công trước.
- **Branch-and-Bound (Nhánh và Cận)**: Các phân công đã được người dùng chấp nhận trước đó được khóa lại dưới dạng ràng buộc cứng. Với các hành khách còn lại, hệ thống duyệt cây quyết định phân công, cập nhật chi phí ước lượng và thực hiện cắt nhánh (pruning) nếu cận dưới lạc quan của nhánh kém nghiệm tốt nhất đang có. Kỹ thuật này giúp hệ thống tìm được phương án phân công tối ưu toàn cục chỉ trong thời gian mili giây.

---

## Slide 14 — Bước 6: Tối ưu tuyến (Open-path TSP) (~75 giây)

Sau khi chốt xong danh sách hành khách cho từng tài xế, bài toán đặt ra là sắp xếp thứ tự đón các hành khách này sao cho tổng quãng đường tài xế đi là ngắn nhất. Đây là bài toán Open-path TSP (mở hành trình bắt đầu từ tài xế và kết thúc tại điểm gặp).

Phương pháp giải được phân loại theo số lượng điểm dừng $s$:
- **Nhóm nhỏ ($s \le 9$ điểm dừng)**: Sử dụng thuật toán Quy hoạch động **Held-Karp** để tìm nghiệm chính xác tuyệt đối. Công thức truy hồi quy hoạch động trạng thái là $dp[mask, j] = \min (dp[mask \setminus \{j\}, i] + T(s_i, s_j))$. Với $s \le 9$, không gian trạng thái cực kỳ nhỏ (chỉ khoảng 4608 phần tử ở ngưỡng $s=9$), thuật toán chạy chưa đến 1 mili giây.
- **Nhóm lớn**: Chuyển sang thuật toán Heuristic cheapest insertion kết hợp tối ưu cục bộ 2-opt.
- **Tập hợp lộ trình (Route Pool)**: Sinh sẵn các tổ hợp hành khách khả thi cho từng tài xế, tính toán trước chi phí lộ trình và dùng bài toán phủ tập hợp (Set Covering) để chọn ra phương án bao phủ tốt nhất, tránh lỗi do quyết định phân công quá sớm.

---

## Slide 15 — Bước 7: Đánh giá gánh nặng (Burden) (~75 giây)

Sau khi có lộ trình di chuyển cụ thể, hệ thống tiến hành đánh giá mức độ bất tiện của từng phương án thông qua chỉ số **Gánh nặng di chuyển (Burden - $B$)**. Chỉ số này được đo lường dựa trên các thành phần bất tiện tổng hợp và được tính toán riêng biệt cho hai vai trò:

- **Với tài xế**, gánh nặng $B_d$ được tính bằng tổng thời gian lái xe thực tế $T_{\text{drive}}$, cộng với thời gian đi vòng phát sinh $\max(0, T_{\text{route}} - T_{\text{direct}})$ nhân hệ số phạt đi vòng $\lambda_\Delta$, và số lần dừng đỗ $N_{\text{stops}}$ nhân hệ số phạt dừng đỗ $\lambda_s$.
- **Với hành khách**, gánh nặng $B_p$ gồm thời gian ngồi xe $T_{\text{ride}}$, thời gian đi bộ $T_{\text{walk}}$ nhân hệ số phạt đi bộ $\lambda_w$ (mặc định bằng 2.0), thời gian chờ đợi $T_{\text{wait}}$ nhân hệ số phạt chờ đợi $\lambda_q$ (mặc định bằng 1.5), và rủi ro tiếp cận điểm đón $R_{\text{access}}$.

Các hệ số phạt này giúp phản ánh chính xác tâm lý bất tiện thực tế của người dùng. Chỉ số Burden của mỗi thành viên chính là nền tảng cốt lõi để hệ thống đánh giá mức độ công bằng của phương án di chuyển thông qua hệ số Gini.

---

## Slide 16 — Biên Pareto & Nhãn Giải Thích (~60 giây)

Nếu chúng ta gộp gánh nặng di chuyển và độ công bằng thành một điểm số duy nhất, người dùng sẽ không thấy được sự đánh đổi. Optigo giải quyết bằng cách áp dụng **Biên Pareto**.

Hệ thống sử dụng thuật toán lọc Pareto để loại bỏ tất cả các phương án bị thống trị (dominated) bởi các phương án khác trên mọi khía cạnh. Tập hợp các điểm nằm trên biên Pareto thể hiện các phương án tối ưu không bị thống trị. 

Để giúp người dùng bình thường không cần hiểu thuật toán vẫn chọn được hành trình, hệ thống tự động gắn các nhãn giải thích ngữ nghĩa:
- **⚡ Nhanh nhất**: Phương án có tổng thời gian di chuyển nhỏ nhất.
- **⚖️ Cân bằng**: Điểm cân bằng tối ưu giữa thời gian và hệ số công bằng Gini.
- **🚗 Ít đi vòng**: Phương án ưu tiên giảm thiểu gánh nặng đi vòng của các tài xế.
- **Công bằng nhất**: Phương án tối thiểu hóa sự bất bình đẳng gánh nặng giữa các thành viên.

---

## Slide 17 — Phương án đầu ra trực quan (~30 giây)

Phương án đề xuất được trực quan hóa rất rõ ràng trên bản đồ Mapbox với các tuyến đường đi chung xe được tô màu riêng biệt cho từng tài xế, cùng vị trí các điểm đón và địa điểm gặp mặt.

Phía bên phải là các thẻ thông tin phương án tương ứng với các nhãn giải thích Pareto như "Nhanh nhất", "Cân bằng" giúp nhóm nhanh chóng so sánh. Khi một thành viên biểu quyết, trạng thái lập tức đồng bộ thời gian thực cho toàn nhóm thông qua SignalR mà không cần tải lại trang.

---

## Slide 18 — Thiết lập thực nghiệm (~45 giây)

Để chứng minh tính thực tiễn và tính chính xác của các thuật toán đề xuất, thực nghiệm được tiến hành trên 120 kịch bản benchmark chuẩn hóa:
- **96 kịch bản từ DARP-MP** (Dial-a-Ride Problem with Meeting Points): Đây là bộ dữ liệu sát nhất với thực tế bài toán gặp mặt nhóm kết hợp ghép xe.
- **24 kịch bản khó từ Li-Lim**: Dùng để kiểm tra độ ổn định của thuật toán dưới các ràng buộc khắt khe về mặt thời gian.
Hệ thống được so sánh đối đầu trực tiếp với ba bộ giải tối ưu hóa vận tải hàng đầu thế giới hiện nay là Google OR-Tools, PyVRP và VROOM trên cùng một cấu hình phần cứng CPU/RAM.

---

## Slide 19 — Kết quả tổng quan (~60 giây)

Kết quả thực nghiệm trên 120 kịch bản cho thấy những cải tiến rất rõ rệt của Optigo so với các bộ giải cơ sở:
- **Tỷ lệ hợp lệ (Feasibility)**: Optigo đạt tỷ lệ tìm thấy phương án hợp lệ cao nhất với **95.8%**, vượt qua OR-Tools (90.8%) và vượt trội so với VROOM/PyVRP (81.7%).
- **Chi phí trung bình**: Chi phí của Optigo là **5269 đơn vị**, thấp nhất trong cả bốn phương án so sánh.
- **Chỉ số công bằng trung bình**: Chỉ số công bằng Gini của Optigo đạt **826**, tốt hơn OR-Tools (999) và vượt trội hơn PyVRP/VROOM (1199).
- **Thời gian xử lý (Latency)**: Optigo chỉ mất trung bình **953 mili giây** (dưới 1 giây) để đưa ra kết quả, trong khi OR-Tools mất hơn 8 giây và PyVRP mất hơn 9 giây. Tốc độ này hoàn toàn đáp ứng được tiêu chuẩn của một ứng dụng web tương tác thời gian thực.

---

## Slide 20 — So sánh trực tiếp (~65 giây)

Khi so sánh trực tiếp (pairwise) trên cùng các kịch bản mà cả hai bộ giải cùng giải được:
- So với OR-Tools, Optigo cho ra phương án công bằng hơn trong 95/109 kịch bản chung, đồng thời cải thiện chi phí trung bình thêm 3.9%.
- So với PyVRP và VROOM, Optigo cho ra phương án công bằng hơn trong 92/98 kịch bản chung, và giảm chi phí trung bình hơn 5.7%.
- Điều này chứng minh một kết quả rất quan trọng: **Optigo cải thiện đáng kể tính công bằng mà không hề làm tăng tổng chi phí thời gian di chuyển của nhóm**.
- Nghiên cứu loại bỏ cấu phần (Ablation Study) chỉ ra rằng: việc đưa ràng buộc công bằng vào mô hình chỉ làm tăng 1.3% tổng chi phí di chuyển, đổi lại giúp giảm đến 3.5% gánh nặng lớn nhất và giảm 7.1% độ tiếc nuối cực đại của các thành viên.

---

## Slide 21 — Demo (~25 giây)

Sau đây, em xin phép được trình chiếu demo thực tế hoạt động của ứng dụng Optigo để thầy cô thấy rõ các tính năng tương tác phòng nhóm thời gian thực, cập nhật vị trí, vai trò, bình chọn địa điểm và vẽ lộ trình trực tiếp trên nền bản đồ Mapbox.

*(Bắt đầu bật demo hoặc thao tác trực tiếp trên trình duyệt)*

---

## Slide 22 — Cảm ơn thầy đã lắng nghe! (~20 giây)

Tóm lại, khóa luận của em đã chứng minh được việc gặp mặt nhóm là bài toán ra quyết định nhóm có ràng buộc phức tạp, nơi sự công bằng cần được đặt lên hàng đầu bên cạnh tính tối ưu chi phí. Em xin chân thành cảm ơn thầy cô đã chú ý lắng nghe. Em rất mong nhận được những nhận xét, đóng góp ý kiến từ Hội đồng để đề tài hoàn thiện hơn. Em xin phép bước vào phần trả lời câu hỏi phản biện.

---

> **Tổng thời lượng thuyết trình ước tính: ~15 phút 05 giây**
