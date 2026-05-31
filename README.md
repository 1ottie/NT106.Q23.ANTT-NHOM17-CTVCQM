# NOTE UPDATE 31/5

* vẫn còn vài lỗi undo/redo (nét undo quá nhỏ ->sẽ tối ưu sau)
* bấm undo, replay xong nét vẽ pen ko đồng bộ được nữa
* vào phòng load canvas siêu chậm
* bấm tool text vào text cũ để sửa size, màu xong thì bị lặp trên máy kia
* đọc file LAN_CONNECTION_GUIDE.md để biết cách đổi ip và kết nối mạng

* khi chạy nhớ đổi tk MySQL trong DrawServer/AppConfig.cs, DrawServer/config.ini và MasterServer/appsettings.json

<div align="center">

<img width="100%" height="200" alt="InkSync Banner" src="" />

[![C#](https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![.NET Core](https://img.shields.io/badge/.NET_Core-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/WPF-Windows-0078D7?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/dotnet/wpf)
[![MySQL](https://img.shields.io/badge/MySQL-4479A1?style=for-the-badge&logo=mysql&logoColor=white)](https://www.mysql.com/)
[![Google Cloud](https://img.shields.io/badge/Google_Vision_API-4285F4?style=for-the-badge&logo=google-cloud&logoColor=white)](https://cloud.google.com/vision)

*A high-performance, real-time collaborative drawing platform featuring Master-Node distributed architecture and AI-assisted OCR integration.* 

[Key Features](#-key-features) • [Architecture](#-system-architecture) • [Runbook](#-runbook--installation) • [Team](#-author--team)

</div>

## Project Overview

**InkSync** is a robust desktop application designed to provide a seamless, real-time collaborative drawing experience. Built on a distributed Master-Node architecture, the system efficiently handles heavy TCP socket traffic for real-time strokes while utilizing HTTP RESTful APIs for routing and authentication. 

This project also integrates AI as a supporting tool for research purposes, utilizing Google Vision API to accurately scan, identify, and interact with handwritten or rendered text on the canvas. 

## Disclaimer

**IMPORTANT NOTICE**: This software is developed primarily as an academic and research project. 

- The project is **Open Source** and intended for educational reference.
- It utilizes free-tier APIs (Google Cloud Vision) and local MySQL databases.
- The repository showcases the practical implementation of Information Security and distributed system concepts. It is not intended for commercial deployment without further infrastructure scaling.

## Key Features

<div align="center">
  <table>
    <tr>
      <td width="25%" align="center">
        <h2>⚡</h2>
        <b>Real-time Synchronization</b>
        <br/>
        <sub>Low-latency TCP Socket connections ensuring instant stroke-by-stroke updates across all clients.</sub>
      </td>
      <td width="25%" align="center">
        <h2>⚖️</h2>
        <b>Smart Load Balancing</b>
        <br/>
        <sub>Master API Gateway dynamically routes users to the most available Node Server based on active traffic.</sub>
      </td>
      <td width="25%" align="center">
        <h2>👁️</h2>
        <b>AI-Assisted OCR</b>
        <br/>
        <sub>Select and extract text directly from the canvas using asynchronous Google Vision integration.</sub>
      </td>
      <td width="25%" align="center">
        <h2>🛡️</h2>
        <b>Secure Authentication</b>
        <br/>
        <sub>Stateless JWT (JSON Web Token) implementation ensuring secure room access and data integrity.</sub>
      </td>
    </tr>
  </table>
</div>

## Tech Stack

- **Client Presentation:** WPF (Windows Presentation Foundation), C#, XAML.
- **Master Server (API Gateway):** ASP.NET Core Web API, JWT Authentication.
- **Node Server (Draw/Socket Engine):** C# `TcpListener` / `NetworkStream` for multithreading.
- **Database:** MySQL Server (managed via `MySql.Data` & Dapper).
- **External Services:** Google Cloud Vision API.

## System Architecture

The application is strictly decoupled into Microservices to ensure scalability and high availability.

```mermaid
graph TD
```

## Runbook / Installation

To run this project locally, you must initialize the Database, configure the API Gateway, and start the services in the correct order.

### Phase 1: Database Initialization
1. Ensure **MySQL Server** is running on your machine.
2. Open your preferred SQL client (e.g., MySQL Workbench, DBeaver).
3. Execute the `online_Drawing_DB.sql` script located in the `Database/` folder to generate the required schema (`Users`, `Rooms`, `Nodes`, `DrawActions`, `Messages`).

### Phase 2: Master Server Configuration
1. Navigate to the `MasterServer` directory.
2. Rename `appsettings.Example.json` to `appsettings.json`.
3. Update the configuration file with your specific credentials:
   ```json
   {
     "GoogleVision": {
       "ApiKey": "YOUR_GOOGLE_CLOUD_VISION_API_KEY" 
     },
     "ConnectionStrings": {
       "DefaultConnection": "server=127.0.0.1;database=online_Drawing_DB;user=root;password="
     },
     "Jwt": {
       "Secret": "A_VERY_LONG_SECRET_KEY_MINIMUM_32_CHARS"
     }
   }
   ```

### Phase 3: Launching the Ecosystem
Using Visual Studio 2022:
1. Open the solution file (`.sln`).
2. Right-click the Solution in Solution Explorer -> **Properties** -> **Startup Project**.
3. Select **Multiple startup projects**.
4. Set the action for both **`MasterServer`** and **`DrawServer`** to **Start**.
5. Press `F5` to build and run.
   - *Master Server Console* will start on HTTP port `5274`.
   - *Node Server Console* will start on TCP port `6001` and print a green log: `[NODE SERVER] TỰ ĐỘNG ĐĂNG KÝ THÀNH CÔNG!` indicating it has successfully linked with the Master Server.

### Phase 4: Client Connection
1. Run the **`DrawClient`** WPF application.
2. Create an account or log in.
3. Create a new Drawing Room. The Master Server will allocate you to the active Node Server.
4. Enjoy real-time sketching and AI-assisted text extraction!

## Academic Focus & Learning Outcomes

This project is a practical application of advanced computer science and network security principles:
- **Distributed Systems:** Handling state synchronization across multiple independent servers.
- **Asynchronous Programming:** Ensuring non-blocking UI threads during heavy network payloads or external API calls (`async/await`).
- **Network Security:** Secure payload transmission and identity verification using JWT.

## Author & Team

<div align="center">


</div>

## Contact
For academic inquiries, architecture discussions, or collaboration:
- **Location:** Ho Chi Minh City, Vietnam.
- **Institution:** VNU-HCM, University of Information Technology.