# R65 — Cut thật khớp Preview tại Bend và contour bậc

Log fr5 × fr1a cho thấy Sketch hợp lệ nhưng Normal Cut để lại lát tam giác 110,1007 mm³ mà Preview prism không mô tả đúng. Biên Sketch đã dừng tại góc sắc ảo của cạnh gấp. Với contour bậc ở phía còn lại, Normal Cut còn tạo mặt cong; một khối prism hoặc một map Bend theo mặt phẳng cố định không đủ.

Source cuối giữ cùng class và luồng A/B. Các biên đủ chứng cứ được mở rộng đến mặt tôn đối diện hữu hạn của nếp kề, giới hạn bằng API t/R và cặp mặt 0/t. Sketch dùng mặt Face2 có sẵn và ràng buộc hình học tới guide/điểm Frame thực. Không tạo RefPlane, Convert, Fix, Through All hay Edge Flange. Có phân biệt contour chữ nhật bằng đủ bốn góc trong hệ trục thực, không dựa vào tên RETURN_MITER.

Preview dựng sector Bend từ cylinder R/R+t và tangent B-rep. Với contour bậc, từng khoảng góc được chia tại sự kiện vertex của guide; lower envelope trên mỗi khoảng là đường thẳng xác định. Mặt đáy radial được biểu diễn bằng NURBS hữu tỉ chính xác, cùng tọa độ dọc Frame trên mọi bán kính ở một góc. Extrude mặt tạm này tạo Body cutter; không phải một Feature Cut sâu hoặc Through All. Mặt NURBS được kiểm phương trình trước Boolean, rồi Native Cut vẫn phải khớp target bằng B-rep trong ngưỡng 1e-12 m³. Khi số đo thể tích tích phân khác nhau, phân hoạch được kiểm lại bằng missing/overlap/outside Body thực với cùng ngưỡng, không nới tolerance.

Cut thật vẫn từng lớp: Blind = bề dày đọc API + 0,10 mm, NormalCut=true, FlipSideToCut=false. AssemblyFitGap=0,10 mm và SheetCutOverrun=0,10 mm là hai tham số riêng. Lát tiếp theo đóng Sketch theo mép của skin thực đang còn, có chứng minh cutter đóng trên stock-cap vẫn loại bỏ đúng vùng đã duyệt; không dùng chiều dài phần khác của Body làm đầu phôi của skin đã cắt ngắn.

Source cuối đã qua cặp lỗi 4332 fr5 × fr1a và đủ bốn góc 2354 trên cùng Assembly bản sao. Xem test matrix và inventory từng Cut: native/Preview difference bằng 0, Body Check2/Check3 hợp lệ, khe hở 0,10 mm và zero interference; toàn bộ Sketch và lịch sử gốc được kiểm lại ở cuối lượt. Đây là API harness gọi class thật, chưa phải lượt dùng Add-in sau người dùng rebuild.

Các thử nghiệm rollback chưa đạt không được chép vào final; journal của R64 giữ nguyên byte-normalized. Các báo cáo chẩn đoán giữ riêng để Code Owner thấy cả FAIL/UNVERIFIED. Chỉ class Ghost được chép vào source chính; không main Build/Deploy, sửa project hoặc DLL/PDB, Save CAD hay Merge. DLL hiện tại vẫn cần người dùng rebuild để chứa R65.

Xem [API evidence](API_EVIDENCE.json), [test matrix](TEST_MATRIX.json), [inventory Cut tạo mới](GENERATED_CUT_INVENTORY.json), [source/DLL](SOURCE_VERIFICATION.json) và [giới hạn](LIMITATIONS.md).
