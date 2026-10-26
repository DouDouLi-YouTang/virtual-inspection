package main

import "time"

type Position struct {
	X float64 `json:"x"`
	Y float64 `json:"y"`
	Z float64 `json:"z"`
}

type Issue struct {
	ID          string    `gorm:"primaryKey" json:"id"`
	Title       string    `json:"title"`
	Description string    `json:"description"`
	Priority    string    `json:"priority"`
	Status      string    `json:"status"`
	Position    Position  `gorm:"serializer:json;type:text" json:"position"`
	CreatedAt   time.Time `json:"createdAt"`
	UpdatedAt   time.Time `json:"updatedAt"`
}

type CreateIssueRequest struct {
	Title       string   `json:"title"`
	Description string   `json:"description"`
	Priority    string   `json:"priority"`
	Position    Position `json:"position"`
}

type UpdateIssueRequest struct {
	Status string `json:"status"`
}

type ErrorResponse struct {
	Error string `json:"error"`
}

