import type { CapacitorConfig } from "@capacitor/cli";

/**
 * The phone app. appId is the store identity: it must be unique, and it cannot change once
 * the app is published — change it here, before the first upload, if you want another one.
 */
const config: CapacitorConfig = {
  appId: "az.watchingyou.cinema",
  appName: "WatchingYou",
  webDir: "dist-mobile",
  android: {
    // Served from https://localhost inside the app, which is the origin the API's CORS
    // policy allows for Android.
    // (iOS uses capacitor://localhost.)
  },
  server: {
    androidScheme: "https",
  },
  plugins: {
    SplashScreen: {
      launchShowDuration: 1200,
      backgroundColor: "#0A0C0A",
      showSpinner: false,
    },
    StatusBar: {
      backgroundColor: "#0A0C0A",
      overlaysWebView: false,
    },
  },
};

export default config;
