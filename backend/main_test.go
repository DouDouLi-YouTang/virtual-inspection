package main

import (
	"bytes"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"testing"

	"gorm.io/gorm"
)

type testAPI struct {
	router http.Handler
	db     *gorm.DB
	path   string
}

func setupAPI(t *testing.T) testAPI {
	t.Helper()
	path := filepath.Join(t.TempDir(), "test.db")
	db, err := openDatabase(path)
	if err != nil {
		t.Fatalf("open database: %v", err)
	}
	t.Cleanup(func() {
		sqlDB, err := db.DB()
		if err == nil {
			_ = sqlDB.Close()
		}
	})
	return testAPI{router: newRouter(db), db: db, path: path}
}

func (api testAPI) request(t *testing.T, method, path string, body any) *httptest.ResponseRecorder {
	t.Helper()
	var payload []byte
	if body != nil {
		var err error
		payload, err = json.Marshal(body)
		if err != nil {
			t.Fatalf("marshal request: %v", err)
		}
	}
	request := httptest.NewRequest(method, path, bytes.NewReader(payload))
	request.Header.Set("Content-Type", "application/json")
	response := httptest.NewRecorder()
	api.router.ServeHTTP(response, request)
	return response
}

func decodeIssue(t *testing.T, response *httptest.ResponseRecorder) Issue {
	t.Helper()
	var issue Issue
	if err := json.Unmarshal(response.Body.Bytes(), &issue); err != nil {
		t.Fatalf("decode issue: %v; body=%s", err, response.Body.String())
	}
	return issue
}

func validIssue() CreateIssueRequest {
	return CreateIssueRequest{
		Title:       "1 号机柜温度异常",
		Description: "机柜右侧温度明显偏高",
		Priority:    "high",
		Position:    Position{X: 1.25, Y: 0.8, Z: 2.4},
	}
}

func TestHealth(t *testing.T) {
	api := setupAPI(t)
	response := api.request(t, http.MethodGet, "/api/health", nil)
	if response.Code != http.StatusOK {
		t.Fatalf("status = %d, body = %s", response.Code, response.Body.String())
	}
	if response.Body.String() != `{"status":"ok"}` {
		t.Fatalf("unexpected body: %s", response.Body.String())
	}
}

func TestCreateAndListIssue(t *testing.T) {
	api := setupAPI(t)
	response := api.request(t, http.MethodPost, "/api/issues", validIssue())
	if response.Code != http.StatusCreated {
		t.Fatalf("create status = %d, body = %s", response.Code, response.Body.String())
	}
	issue := decodeIssue(t, response)
	if issue.ID == "" || issue.Status != "open" || issue.Title != "1 号机柜温度异常" {
		t.Fatalf("unexpected created issue: %+v", issue)
	}
	if issue.Position != validIssue().Position {
		t.Fatalf("position mismatch: %+v", issue.Position)
	}
	if issue.CreatedAt.IsZero() || issue.UpdatedAt.IsZero() {
		t.Fatalf("timestamps were not generated: %+v", issue)
	}

	listResponse := api.request(t, http.MethodGet, "/api/issues", nil)
	if listResponse.Code != http.StatusOK {
		t.Fatalf("list status = %d", listResponse.Code)
	}
	var issues []Issue
	if err := json.Unmarshal(listResponse.Body.Bytes(), &issues); err != nil {
		t.Fatalf("decode list: %v", err)
	}
	if len(issues) != 1 || issues[0].ID != issue.ID {
		t.Fatalf("unexpected list: %+v", issues)
	}
}

func TestUpdateAllStatuses(t *testing.T) {
	api := setupAPI(t)
	created := decodeIssue(t, api.request(t, http.MethodPost, "/api/issues", validIssue()))

	for _, status := range []string{"in_progress", "resolved", "open"} {
		t.Run(status, func(t *testing.T) {
			response := api.request(t, http.MethodPatch, "/api/issues/"+created.ID, UpdateIssueRequest{Status: status})
			if response.Code != http.StatusOK {
				t.Fatalf("status = %d, body = %s", response.Code, response.Body.String())
			}
			updated := decodeIssue(t, response)
			if updated.Status != status {
				t.Fatalf("status = %q, want %q", updated.Status, status)
			}
			if updated.UpdatedAt.Before(created.UpdatedAt) {
				t.Fatalf("updatedAt moved backwards")
			}
		})
	}
}

func TestValidationErrors(t *testing.T) {
	api := setupAPI(t)
	tests := []struct {
		name string
		body CreateIssueRequest
		want string
	}{
		{"empty title", CreateIssueRequest{Priority: "low"}, "title is required"},
		{"blank title", CreateIssueRequest{Title: "   ", Priority: "low"}, "title is required"},
		{"bad priority", CreateIssueRequest{Title: "test", Priority: "urgent"}, "priority must be one of low, medium, high"},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			response := api.request(t, http.MethodPost, "/api/issues", test.body)
			if response.Code != http.StatusBadRequest {
				t.Fatalf("status = %d", response.Code)
			}
			var errorBody ErrorResponse
			if err := json.Unmarshal(response.Body.Bytes(), &errorBody); err != nil {
				t.Fatalf("decode error: %v", err)
			}
			if errorBody.Error != test.want {
				t.Fatalf("error = %q, want %q", errorBody.Error, test.want)
			}
		})
	}
}

func TestInvalidStatusAndMissingIssue(t *testing.T) {
	api := setupAPI(t)
	created := decodeIssue(t, api.request(t, http.MethodPost, "/api/issues", validIssue()))

	badStatus := api.request(t, http.MethodPatch, "/api/issues/"+created.ID, UpdateIssueRequest{Status: "closed"})
	if badStatus.Code != http.StatusBadRequest {
		t.Fatalf("invalid status returned %d", badStatus.Code)
	}

	missing := api.request(t, http.MethodPatch, "/api/issues/not-found", UpdateIssueRequest{Status: "open"})
	if missing.Code != http.StatusNotFound {
		t.Fatalf("missing issue returned %d", missing.Code)
	}
}

func TestDataSurvivesDatabaseReopen(t *testing.T) {
	api := setupAPI(t)
	created := decodeIssue(t, api.request(t, http.MethodPost, "/api/issues", validIssue()))
	sqlDB, err := api.db.DB()
	if err != nil {
		t.Fatalf("get sql db: %v", err)
	}
	if err := sqlDB.Close(); err != nil {
		t.Fatalf("close database: %v", err)
	}

	reopened, err := openDatabase(api.path)
	if err != nil {
		t.Fatalf("reopen database: %v", err)
	}
	t.Cleanup(func() {
		sqlDB, err := reopened.DB()
		if err == nil {
			_ = sqlDB.Close()
		}
	})
	var issue Issue
	if err := reopened.First(&issue, "id = ?", created.ID).Error; err != nil {
		t.Fatalf("load persisted issue: %v", err)
	}
	if issue.Title != created.Title {
		t.Fatalf("persisted title = %q", issue.Title)
	}
}

func TestDeleteIssue(t *testing.T) {
	api := setupAPI(t)
	created := decodeIssue(t, api.request(t, http.MethodPost, "/api/issues", validIssue()))

	deleted := api.request(t, http.MethodDelete, "/api/issues/"+created.ID, nil)
	if deleted.Code != http.StatusNoContent {
		t.Fatalf("delete status = %d, body = %s", deleted.Code, deleted.Body.String())
	}

	listResponse := api.request(t, http.MethodGet, "/api/issues", nil)
	var issues []Issue
	if err := json.Unmarshal(listResponse.Body.Bytes(), &issues); err != nil {
		t.Fatalf("decode list: %v", err)
	}
	if len(issues) != 0 {
		t.Fatalf("expected empty list after delete, got %+v", issues)
	}

	missing := api.request(t, http.MethodDelete, "/api/issues/"+created.ID, nil)
	if missing.Code != http.StatusNotFound {
		t.Fatalf("second delete status = %d", missing.Code)
	}
}

