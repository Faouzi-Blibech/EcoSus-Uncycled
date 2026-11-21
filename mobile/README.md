# Uncycled — Mobile Frontend

## Quick Start (Hackathon Setup)

```bash
cd mobile
npx create-expo-app@latest uncycled-app --template blank
cd uncycled-app

# Install dependencies
npx expo install expo-camera expo-image-manipulator react-native-svg @react-native-async-storage/async-storage expo-crypto
npm install axios

# Replace App.js with our App.js (one level up)
copy ..\..\mobile\App.js .\App.js
# Copy the services and components folders in too
xcopy ..\..\mobile\services .\services /E /I
xcopy ..\..\mobile\components .\components /E /I

# Start
npx expo start
```

## Configuration
Edit `services/api.js` and set `API_BASE_URL` to your FastAPI server's IP address.

The server owns the streak, CO₂ totals and skins. The app only keeps an anonymous
player id (a UUID in AsyncStorage) and sends it as `X-Player-Id` with every request.

## Architecture
```
App.js                    — Main app shell, navigation between camera and result
components/
  CameraScreen.js         — Native camera capture (live photo only, no gallery)
  ResultScreen.js         — Displays verification result + cosmetic unlock
  DetectionOverlay.js     — Photo with the detected item's outline drawn on it
  SkinUnlockOverlay.js    — Animated cosmetic skin unlock celebration
services/
  api.js                  — Axios client + multipart upload to /verify-daily-photo
  playerId.js             — Anonymous player id kept on the device
```
