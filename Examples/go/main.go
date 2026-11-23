package main

import (
	"fmt"
	"io"
	"log"
	"strings"

	"github.com/noyzone99/Vessel/Bindings/go"
)

func main() {
	if err := run(); err != nil {
		log.Fatal(err)
	}
}

func run() error {
	const key = "documents/hello.txt"
	const message = "Hello from Vessel!"
	store, err := vessel.Open("my-store", nil)
	if err != nil {
		return err
	}
	defer store.Close()

	_, err = store.Put(key, strings.NewReader(message), &vessel.WriteOptions{Overwrite: true})
	if err != nil {
		return err
	}
	content, err := store.Open(key)
	if err != nil {
		return err
	}
	defer content.Close()
	stored, err := io.ReadAll(content)
	if err != nil {
		return err
	}
	if string(stored) != message {
		return fmt.Errorf("stored text differs from the original")
	}
	fmt.Println(string(stored))
	return nil
}
