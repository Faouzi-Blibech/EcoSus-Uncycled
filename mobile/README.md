# Uncycled — Mobile Frontend

## Quick Start (Hackathon Setup)

```bash
cd mobile
npx create-expo-app@latest uncycled-app --template blank
cd uncycled-app

# Install dependencies
npx expo install expo-camera expo-image-manipulator
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

## Architecture
```
App.js                    — Main app shell, navigation between camera and result
components/
  CameraScreen.js         — Native camera capture (live photo only, no gallery)
  ResultScreen.js         — Displays verification result + cosmetic unlock
  SkinUnlockOverlay.js    — Animated cosmetic skin unlock celebration
services/
  api.js                  — Axios client + multipart upload to /verify-daily-photo
  streakManager.js        — Local streak counter + cosmetic skin unlock logic
```
