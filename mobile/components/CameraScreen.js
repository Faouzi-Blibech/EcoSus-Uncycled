/**
 * CameraScreen.js — Live Camera Capture (No Gallery Access)
 *
 * Uses expo-camera to present a full-screen viewfinder and capture
 * a single photo. The captured image URI is passed directly to the
 * parent via onPhotoCaptured — it never touches the media library.
 *
 * This enforces the "live capture" rule: players must photograph a
 * real recycling item right now, not pick an old photo from their gallery.
 */
import React, { useState, useRef } from "react";
import {
  View,
  Text,
  TouchableOpacity,
  StyleSheet,
  ActivityIndicator,
  SafeAreaView,
  StatusBar,
} from "react-native";
import { CameraView, useCameraPermissions } from "expo-camera";

export default function CameraScreen({ onPhotoCaptured }) {
  const [permission, requestPermission] = useCameraPermissions();
  const [isCapturing, setIsCapturing] = useState(false);
  const cameraRef = useRef(null);

  // ─── Permission Gate ────────────────────────────────────────
  if (!permission) {
    // Permissions are still loading
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color="#2ECC71" />
      </View>
    );
  }

  if (!permission.granted) {
    return (
      <SafeAreaView style={styles.permissionContainer}>
        <StatusBar barStyle="light-content" />
        <Text style={styles.permissionEmoji}>📸</Text>
        <Text style={styles.permissionTitle}>Camera Access Needed</Text>
        <Text style={styles.permissionText}>
          Uncycled needs your camera to verify recycling items.{"\n"}
          No gallery access — live photos only!
        </Text>
        <TouchableOpacity style={styles.grantButton} onPress={requestPermission}>
          <Text style={styles.grantButtonText}>Grant Camera Access</Text>
        </TouchableOpacity>
      </SafeAreaView>
    );
  }

  // ─── Capture Handler ────────────────────────────────────────
  const handleCapture = async () => {
    if (!cameraRef.current || isCapturing) return;

    setIsCapturing(true);
    try {
      // takePictureAsync returns { uri, width, height, exif? }
      // quality 0.7 keeps payload small for faster upload
      const photo = await cameraRef.current.takePictureAsync({
        quality: 0.7,
        skipProcessing: false,
      });
      onPhotoCaptured(photo.uri);
    } catch (error) {
      console.error("Camera capture failed:", error);
    } finally {
      setIsCapturing(false);
    }
  };

  // ─── Render ─────────────────────────────────────────────────
  return (
    <View style={styles.container}>
      <StatusBar barStyle="light-content" />
      <CameraView
        ref={cameraRef}
        style={styles.camera}
        facing="back"
      >
        {/* Viewfinder Overlay */}
        <SafeAreaView style={styles.overlay}>
          <View style={styles.topBar}>
            <Text style={styles.topBarText}>♻️ Scan Your Recycling</Text>
            <Text style={styles.hint}>
              Point at a bottle, can, box, or jar
            </Text>
          </View>

          {/* Targeting Reticle */}
          <View style={styles.reticleContainer}>
            <View style={styles.reticle} />
          </View>

          {/* Shutter Button */}
          <View style={styles.bottomBar}>
            <TouchableOpacity
              style={[
                styles.shutterButton,
                isCapturing && styles.shutterButtonDisabled,
              ]}
              onPress={handleCapture}
              disabled={isCapturing}
              activeOpacity={0.7}
            >
              {isCapturing ? (
                <ActivityIndicator size="small" color="#000" />
              ) : (
                <View style={styles.shutterInner} />
              )}
            </TouchableOpacity>
            <Text style={styles.shutterLabel}>
              {isCapturing ? "Capturing..." : "Tap to Capture"}
            </Text>
          </View>
        </SafeAreaView>
      </CameraView>
    </View>
  );
}

// ─── Styles ───────────────────────────────────────────────────
const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: "#000",
  },
  centered: {
    flex: 1,
    justifyContent: "center",
    alignItems: "center",
    backgroundColor: "#1a1a2e",
  },
  camera: {
    flex: 1,
  },
  overlay: {
    flex: 1,
    justifyContent: "space-between",
  },
  topBar: {
    alignItems: "center",
    paddingTop: 16,
    paddingHorizontal: 20,
    backgroundColor: "rgba(0,0,0,0.5)",
    paddingBottom: 12,
  },
  topBarText: {
    color: "#fff",
    fontSize: 20,
    fontWeight: "bold",
  },
  hint: {
    color: "#ccc",
    fontSize: 14,
    marginTop: 4,
  },
  reticleContainer: {
    flex: 1,
    justifyContent: "center",
    alignItems: "center",
  },
  reticle: {
    width: 220,
    height: 220,
    borderWidth: 2,
    borderColor: "rgba(46, 204, 113, 0.7)",
    borderRadius: 20,
    backgroundColor: "transparent",
  },
  bottomBar: {
    alignItems: "center",
    paddingBottom: 30,
    backgroundColor: "rgba(0,0,0,0.5)",
    paddingTop: 16,
  },
  shutterButton: {
    width: 72,
    height: 72,
    borderRadius: 36,
    backgroundColor: "#fff",
    justifyContent: "center",
    alignItems: "center",
    borderWidth: 4,
    borderColor: "#2ECC71",
  },
  shutterButtonDisabled: {
    opacity: 0.5,
  },
  shutterInner: {
    width: 56,
    height: 56,
    borderRadius: 28,
    backgroundColor: "#2ECC71",
  },
  shutterLabel: {
    color: "#aaa",
    fontSize: 12,
    marginTop: 8,
  },
  // Permission screen
  permissionContainer: {
    flex: 1,
    justifyContent: "center",
    alignItems: "center",
    backgroundColor: "#1a1a2e",
    paddingHorizontal: 32,
  },
  permissionEmoji: {
    fontSize: 64,
    marginBottom: 16,
  },
  permissionTitle: {
    color: "#fff",
    fontSize: 24,
    fontWeight: "bold",
    marginBottom: 12,
  },
  permissionText: {
    color: "#aaa",
    fontSize: 16,
    textAlign: "center",
    lineHeight: 24,
    marginBottom: 32,
  },
  grantButton: {
    backgroundColor: "#2ECC71",
    paddingHorizontal: 32,
    paddingVertical: 14,
    borderRadius: 25,
  },
  grantButtonText: {
    color: "#fff",
    fontSize: 16,
    fontWeight: "bold",
  },
});
