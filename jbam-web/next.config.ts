import type { NextConfig } from "next"

// 이 화면은 PaymentAlert.exe 의 내 PC 전용 서버(HttpListener)가 정적 파일로 내준다 (ADR-0007).
// Node 서버를 띄우지 않으므로 정적 내보내기만 쓴다.
const nextConfig: NextConfig = {
  output: "export",
  images: { unoptimized: true },
  // 개발 표시기·원격 폰트 등 바깥 주소를 부르는 기능은 쓰지 않는다 (AC-W11).
  devIndicators: false,
}

export default nextConfig
