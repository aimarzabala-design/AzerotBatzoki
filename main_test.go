package main

import (
	"encoding/json"
	"os"
	"path/filepath"
	"reflect"
	"strconv"
	"strings"
	"testing"
)

type launchRecord struct {
	Args   []string
	Script string
}

func TestMain(m *testing.M) {
	if recordPath := os.Getenv("LAUNCHER_TEST_RECORD"); recordPath != "" {
		if strings.Contains(os.Getenv("PSModulePath"), "sentinel-pwsh7") {
			os.Exit(93)
		}
		args := os.Args[1:]
		if len(args) < 6 || args[4] != "-File" {
			os.Exit(90)
		}
		script, err := os.ReadFile(args[5])
		if err != nil {
			os.Exit(91)
		}
		record, _ := json.Marshal(launchRecord{Args: args, Script: string(script)})
		if err := os.WriteFile(recordPath, record, 0600); err != nil {
			os.Exit(92)
		}
		code, _ := strconv.Atoi(os.Getenv("LAUNCHER_TEST_EXIT"))
		os.Exit(code)
	}
	os.Exit(m.Run())
}

func TestLauncherForwardsArgumentsExitCodeAndCleansUniqueDirectory(t *testing.T) {
	root := t.TempDir()
	powershell := filepath.Join(root, "System32", "WindowsPowerShell", "v1.0", "powershell.exe")
	if err := os.MkdirAll(filepath.Dir(powershell), 0700); err != nil {
		t.Fatal(err)
	}
	testExecutable, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	data, err := os.ReadFile(testExecutable)
	if err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(powershell, data, 0700); err != nil {
		t.Fatal(err)
	}
	t.Setenv("SystemRoot", root)
	t.Setenv("PSModulePath", "sentinel-pwsh7")
	// A regression to PATH lookup must fail without reaching the real installer.
	t.Setenv("PATH", root)
	t.Setenv("TMP", root)
	t.Setenv("TMPDIR", root)
	originalArgs := os.Args
	t.Cleanup(func() { os.Args = originalArgs })
	var previousWork string
	for _, code := range []int{0, 23} {
		if code != 0 {
			t.Setenv("SystemRoot", "")
			t.Setenv("WINDIR", root)
		}
		recordPath := filepath.Join(root, "record.json")
		t.Setenv("LAUNCHER_TEST_RECORD", recordPath)
		t.Setenv("LAUNCHER_TEST_EXIT", strconv.Itoa(code))
		os.Args = []string{"launcher.exe", "-CheckOnly", "argument with spaces"}
		if got := run(); got != code {
			t.Fatalf("run() = %d, want %d", got, code)
		}
		recordJSON, err := os.ReadFile(recordPath)
		if err != nil {
			t.Fatal(err)
		}
		var record launchRecord
		if err := json.Unmarshal(recordJSON, &record); err != nil {
			t.Fatal(err)
		}
		wantArgs := []string{"-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", record.Args[5], "-CheckOnly", "argument with spaces"}
		if !reflect.DeepEqual(record.Args, wantArgs) {
			t.Fatalf("arguments = %#v, want %#v", record.Args, wantArgs)
		}
		if record.Script != string(installer) {
			t.Fatal("child did not receive the embedded installer")
		}
		work := filepath.Dir(record.Args[5])
		if work == previousWork {
			t.Fatal("launches reused a temporary directory")
		}
		previousWork = work
		if _, err := os.Stat(work); !os.IsNotExist(err) {
			t.Fatalf("temporary directory remains after child exit: %v", err)
		}
	}
}
