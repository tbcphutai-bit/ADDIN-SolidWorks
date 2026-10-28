# Thời gian thực thi

`TIMINGS.csv` giữ thời gian Preview đầu và Apply từng cặp từ audit; không thay số thiếu bằng 0. Các thời gian prepare/invariant/factory có thể lồng nhau, không cộng chúng thành tổng.

Parser offline `summarize_r62_timings.ps1` chỉ cho so sánh khi hai lượt đều thành công đầy đủ, cùng hash CAD, cùng thứ tự góc, cùng scope, cùng số Cut và source family đã đối chiếu. Thời gian bao gồm logging đồng bộ của harness. Lượt thất bại hoặc fixture khác phải đánh dấu NOT_COMPARABLE.


Lượt đối chiếu cùng fixture lịch sử 2354, cùng 4 góc/38 Cut và scope direct API: baseline **71.630 ms**, R62 **68.909 ms**, giảm **2.721 ms (3,80%)**. Từng góc giảm khoảng 2,20–4,36%. Đây là một cặp lượt đo có logging harness, chưa phải thống kê hiệu năng hoặc đo bên trong DLL sau rebuild.

Ở cặp 4332 đã kiểm: Preview đầu 908 ms; xem lại 3 ms; P2 23 ms; về P1 4 ms; thay camera rồi dựng lại từ cache 7 ms, không gọi solver thêm. Các lượt đầu của những cặp khác có thể khác đáng kể. Khung nhìn và bitmap API được kiểm riêng với Display3; không coi Display3=0 là đủ chứng minh nhìn thấy.
