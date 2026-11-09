package main

import (
	_ "embed"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
)

// installer.ps1 is embedded so the Windows x64 EXE is a single-file launcher.
//
//go:embed installer.ps1
var installer []byte

func main() {
	os.Exit(run())
}

func run() (code int) {
	interactive := true
	for _, arg := range os.Args[1:] {
		if strings.EqualFold(strings.SplitN(arg, ":", 2)[0], "-CheckOnly") {
			interactive = false
		}
	}
	if info, err := os.Stdin.Stat(); err != nil || info.Mode()&os.ModeCharDevice == 0 {
		interactive = false
	}
	defer func() {
		if code != 0 && interactive {
			pause()
		}
	}()

	work, err := os.MkdirTemp("", "WoWServerInstaller-")
	if err != nil {
		fmt.Fprintf(os.Stderr, "No se pudo preparar el instalador: %v\n", err)
		return 1
	}
	defer os.RemoveAll(work)
	scriptPath := filepath.Join(work, "installer.ps1")
	if err := os.WriteFile(scriptPath, installer, 0600); err != nil {
		fmt.Fprintf(os.Stderr, "No se pudo extraer installer.ps1: %v\n", err)
		return 1
	}
	root := os.Getenv("SystemRoot")
	if root == "" {
		root = os.Getenv("WINDIR")
	}
	if !filepath.IsAbs(root) {
		fmt.Fprintln(os.Stderr, "SystemRoot o WINDIR debe indicar una carpeta absoluta de Windows.")
		return 1
	}
	powershell := filepath.Join(root, "System32", "WindowsPowerShell", "v1.0", "powershell.exe")
	args := append([]string{"-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath}, os.Args[1:]...)
	cmd := exec.Command(powershell, args...)
	// Windows PowerShell must build its own module paths, even when launched from PowerShell 7.
	for _, entry := range os.Environ() {
		key, _, _ := strings.Cut(entry, "=")
		if !strings.EqualFold(key, "PSModulePath") {
			cmd.Env = append(cmd.Env, entry)
		}
	}
	cmd.Stdin = os.Stdin
	cmd.Stdout = os.Stdout
	cmd.Stderr = os.Stderr
	if err := cmd.Run(); err != nil {
		fmt.Fprintf(os.Stderr, "El instalador terminó con un error: %v\n", err)
		if exitErr, ok := err.(*exec.ExitError); ok {
			return exitErr.ExitCode()
		}
		return 1
	}
	return 0
}

func pause() {
	fmt.Print("Pulsa ENTER para cerrar...")
	var b = make([]byte, 1)
	_, _ = os.Stdin.Read(b)
}
